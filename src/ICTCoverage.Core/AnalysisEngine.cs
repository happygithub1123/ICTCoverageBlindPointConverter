using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace ICTCoverage.Core;

public sealed class AnalysisEngine(RuleConfig config)
{
    public void Analyze(Analysis a, string profile)
    {
        config.Validate();a.Profile=profile;a.RuleVersion=config.Version;a.Issues.Clear();
        foreach(var c in a.Components)
        {
            c.Issues.Clear(); c.Reference=""; c.ReasonCode=""; c.RuleId="";c.ManuallyReviewed=false;c.MatchedRules.Clear();c.AppliedRuleIds=["F-01","N-01","N-02","V-02","V-03"];
            var match=Regex.Match(c.RawReference.Trim(),@"^(?<ref>(?<prefix>[A-Za-z]+)\d+)(?<pins>(?:_[A-Za-z0-9]+)*)(?<suffix>(?:/.*)?)$");
            if(!match.Success) { c.Reference=c.RawReference; c.Type="待确认"; c.Issues.Add(new("reference","无法识别位号")); }
            else { c.Reference=match.Groups["ref"].Value.ToUpperInvariant(); c.Type=config.PrefixTypes.GetValueOrDefault(match.Groups["prefix"].Value.ToUpperInvariant(),"待确认"); }
            if(match.Success&&match.Groups["pins"].Length>0&&!Regex.IsMatch(match.Groups["pins"].Value,@"^(?:_\d+)+$|^_[A-Za-z]_\d+$"))c.Issues.Add(new("pins","未知管脚/门电路后缀"));
            var suffix=c.RawReference.Split('/').Skip(1).Where(x=>x.Length>0).Select(x=>x.ToUpperInvariant()).ToArray();
            var normalized=new HashSet<string>();
            foreach(var s in suffix)
                if(config.SuffixAliases.TryGetValue(s,out var alias)) normalized.Add(alias);
                else c.Issues.Add(new("suffix",$"未知后缀 /{s}"));
            bool np=normalized.Contains("NP"), no=config.UntestableSymbols.Contains(c.StatusSymbol.Trim(),StringComparer.OrdinalIgnoreCase), yes=config.TestableSymbols.Contains(c.StatusSymbol.Trim(),StringComparer.OrdinalIgnoreCase);
            c.SourceState=no?TestState.Untestable:yes?TestState.Testable:TestState.Unknown;c.State=c.SourceState;
            c.AppliedRuleIds.Add(no?"F-02":yes?"F-03":"F-04");
            if(np&&yes)c.Issues.Add(new("status","NP与源缺件列可测标记冲突；源统计不变，请人工确认"));
            if(c.State==TestState.Unknown) c.Issues.Add(new("status","空白或未知检测状态"));
            if(c.Yellow && c.State!=TestState.Untestable) c.Issues.Add(new("yellow","黄色底纹与不可测判定不一致",false));
            c.Value=config.TypeValues.GetValueOrDefault(c.Type,c.RawValue);
            if(c.Type=="电阻" && config.Rules.Any(r=>r.RuleId=="V-01"&&r.Enabled) && config.ProfileValueOverrides.TryGetValue(profile,out var overrides)){c.Value=overrides.GetValueOrDefault(c.RawValue,c.RawValue);c.AppliedRuleIds.Add("V-01");}
            if(c.State!=TestState.Testable)
            {
                if(c.Type=="待确认") c.Issues.Add(new("type","未知元件前缀"));
                if(string.IsNullOrWhiteSpace(c.RawValue)) c.Issues.Add(new("value","标准值为空"));
                var matches=config.Rules.Where(r=>r.Enabled && (r.Stage is "原因分类" or "可测性扩展") && (r.Profile==null||r.Profile==profile) && r.Types.Contains(c.Type) &&
                    (r.Prefixes.Length==0||r.Prefixes.Contains(match.Groups["prefix"].Value.ToUpperInvariant())) &&
                    (r.Suffix==null||normalized.Contains(r.Suffix)) && !r.ExcludeSuffixes.Any(normalized.Contains) && (!r.NoOtherSuffixes||normalized.Count==0) &&
                    (r.ExactValue==null||r.ExactValue==c.RawValue) && (r.Reference==null||r.Reference==c.Reference)).OrderBy(r=>r.Priority).ThenBy(r=>r.RuleId,StringComparer.Ordinal).ToList();
                c.MatchedRules=matches.Select(r=>new RuleMatch(r.RuleId,r.Priority,r.Reason,r.Status,r.Source)).ToList();
                c.AppliedRuleIds.AddRange(matches.Select(r=>r.RuleId));
                foreach(var review in matches.Where(r=>r.ReviewOnly))c.Issues.Add(new(review.RuleId,$"{review.RuleId}：{(review.RuleId=="R-03"?"/R的10%口径及测值未确认":review.RuleId=="C-07"?"/C的30%定义待确认，仅作参考，不计算可测性":"SG来源/短路群定义待确认，不自动判定")}",review.BlockingReview));
                var candidates=matches.Where(r=>!r.ReviewOnly&&r.Reason.Length>0).ToList();
                var rule=candidates.FirstOrDefault();
                if(rule==null) c.Issues.Add(new("classification","未匹配盲点原因"));
                else
                {
                    c.ReasonCode=rule.Reason;c.RuleId=rule.RuleId;
                    if(rule.RequiresProgram&&string.IsNullOrWhiteSpace(c.ProgramEvidence))c.Issues.Add(new("program",$"{rule.RuleId}：原因仅为样例建议，缺测试程序证据，需人工复核"));
                    if(rule.ReviewOutsideSample)c.Issues.Add(new("reliability",$"{rule.RuleId}：{rule.Status}，请核对实际元件特性",profile!=config.SampleProfile||c.Reference.StartsWith('X')));
                    if(candidates.Any(r=>r.Reason!=rule.Reason&&!rule.Supersedes.Contains(r.RuleId)))c.Issues.Add(new("rule_conflict","多个规则原因不同且没有已声明的优先关系"));
                }
                if(normalized.Contains("PCB")&&normalized.Any(s=>s is "NP" or "C"))c.Issues.Add(new("suffix_conflict","PCB与其他分类后缀共存，保留候选原因并人工审核"));
            }
        }
        CheckDuplicates(a);
        if(a.ReportTestable.HasValue&&a.ReportTestable!=a.Testable)a.Issues.Add(new("source_count","源表可测统计与逐行重算不一致"));
        if(a.ReportUntestable.HasValue&&a.ReportUntestable!=a.SourceUntestable)a.Issues.Add(new("source_count","源表不可测统计与逐行重算不一致"));
        if(a.ReportCoverage.HasValue&&Math.Abs(a.ReportCoverage.Value-a.Coverage)>0.000001)a.Issues.Add(new("source_coverage","源表覆盖率与逐行重算不一致"));
    }
    public static void CheckDuplicates(Analysis a)
    {
        foreach(var c in a.Components)c.Issues.RemoveAll(i=>i.Code=="duplicate");
        foreach(var group in a.Components.Where(c=>!string.IsNullOrWhiteSpace(c.Reference)).GroupBy(c=>c.Reference).Where(g=>g.Count()>1))
            foreach(var c in group) c.Issues.Add(new("duplicate",$"重复位号 {group.Key}，来源行 {string.Join(",",group.Select(x=>x.SourceRow))}"));
    }
    public static List<Component> Filter(Analysis a,FilterOptions f,RuleConfig cfg) => a.Components.Where(c=>
        (f.Type=="待确认"?c.Pending:(!f.OnlyUntestable||c.State==TestState.Untestable) && (f.Type=="全部"||f.Type==c.Type)) &&
        c.Value.Contains(f.Value,StringComparison.OrdinalIgnoreCase) && cfg.ReasonText(c.ReasonCode).Contains(f.Reason,StringComparison.OrdinalIgnoreCase) &&
        (c.Reference.Contains(f.Reference,StringComparison.OrdinalIgnoreCase)||c.RawReference.Contains(f.Reference,StringComparison.OrdinalIgnoreCase))).ToList();
    public static List<BlindGroup> Group(IEnumerable<Component> items) => items.GroupBy(c=>(c.Value,c.ReasonCode)).Select(g=>new BlindGroup(g.Key.Value,g.Key.ReasonCode,g.OrderBy(c=>c.SourceRow).ToList())).ToList();
    public static void Edit(Analysis a, Component c, string reference,string type,string value,TestState state,string reasonCode,string note,string justification,RuleConfig cfg,string? reviewer=null,string? programEvidence=null)
    {
        if(string.IsNullOrWhiteSpace(justification))throw new InvalidDataException("请填写修改理由");
        if(!Regex.IsMatch(reference,@"^[A-Za-z]+\d+$"))throw new InvalidDataException("规范位号必须为字母前缀加数字，例如 R103");
        if(state is TestState.Unknown or TestState.Conflict)throw new InvalidDataException("请选择明确的可测或不可测状态");
        if(state==TestState.Untestable && (string.IsNullOrWhiteSpace(value)||!cfg.Reasons.ContainsKey(reasonCode)||type=="待确认"))throw new InvalidDataException("不可测记录必须填写类型、标准值和原因");
        string before=JsonSerializer.Serialize(c,RuleConfig.JsonOptions);
        reviewer=string.IsNullOrWhiteSpace(reviewer)?Environment.UserName:reviewer.Trim();
        c.Reference=reference.ToUpperInvariant(); c.Type=type; c.Value=value; c.State=state; c.ReasonCode=state==TestState.Testable?"":reasonCode; c.Note=note;c.ManuallyReviewed=true;c.ProgramEvidence=programEvidence??c.ProgramEvidence;
        c.Issues.RemoveAll(i=>i.Blocking); CheckDuplicates(a);
        a.Edits.Add(new(DateTimeOffset.Now,c.SourceRow,before,JsonSerializer.Serialize(c,RuleConfig.JsonOptions),justification,reviewer,cfg.Version));
    }
}

public static class ReferenceFormatter
{
    public static string Format(IEnumerable<string> references,int width=48)
    {
        if(width<8) throw new ArgumentOutOfRangeException(nameof(width));
        var sb=new StringBuilder(); int length=0;
        foreach(var reference in references)
        {
            if(string.IsNullOrWhiteSpace(reference))continue;
            var token=reference.Trim()+".";
            if(length>0 && length+token.Length>width) {sb.Append('\n');length=0;}
            sb.Append(token);length+=token.Length;
        }
        return sb.ToString();
    }
}
