using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace ICTCoverage.Core;

public sealed class AnalysisEngine(RuleConfig config)
{
    public void Analyze(Analysis a, string profile)
    {
        config.Validate();a.Profile=profile;a.RuleVersion=config.Version;a.Issues.Clear();
        // Resolve inspection/project/type eligibility once, preserving the exact priority order.
        var rulesByType=config.Rules.Where(r=>r.Enabled&&!r.Archived&&r.Inspections.Contains(a.Inspection)&&(r.Stage is "原因分类" or "可测性扩展")&&(r.Profile==null||r.Profile==profile)&&!r.ExcludeProfiles.Contains(profile))
            .OrderBy(r=>r.Priority).ThenBy(r=>r.RuleId,StringComparer.Ordinal).SelectMany(r=>r.Types.Distinct().Select(t=>(Type:t,Rule:r))).ToLookup(p=>p.Type,p=>p.Rule);
        var componentOverrides=config.ComponentOverrides.Where(o=>o.Project==profile).ToLookup(o=>(o.Reference,o.RawValue));
        string resistorType=TypeManagementService.ResolveName(config,"电阻");bool valueOverrideEnabled=config.Rules.Any(r=>r.RuleId=="V-01"&&r.Enabled);
        foreach(var c in a.Components)
        {
            c.Issues.Clear(); c.Reference=""; c.ReasonCode=""; c.RuleId="";c.ManuallyReviewed=false;c.MatchedRules.Clear();c.AppliedRuleIds=["F-01","N-01","N-02","V-02","V-03"];
            var match=Regex.Match(c.RawReference.Trim(),@"^(?<ref>(?<prefix>[A-Za-z]+)\d+)(?<pins>(?:_[A-Za-z0-9]+)*)(?<suffix>(?:/.*)?)$");
            string prefix=match.Success?match.Groups["prefix"].Value.ToUpperInvariant():"";
            if(c.RawReference.Trim().Equals("PCB",StringComparison.OrdinalIgnoreCase)){c.Reference="PCB";c.Type="PCB";}
            else if(!match.Success) { c.Reference=c.RawReference; c.Type="待确认"; c.Issues.Add(new("reference","无法识别位号")); }
            else { c.Reference=match.Groups["ref"].Value.ToUpperInvariant(); c.Type=config.PrefixTypes.GetValueOrDefault(prefix,"待确认"); }
            if(match.Success&&config.ProjectPrefixTypes.TryGetValue(profile,out var prefixMap)&&prefixMap.TryGetValue(prefix,out var mapped))c.Type=mapped;
            var special=componentOverrides[(c.Reference,c.RawValue)].FirstOrDefault();
            if(special?.Type!=null&&!special.Review)c.Type=special.Type;
            if(match.Success&&match.Groups["pins"].Length>0&&!Regex.IsMatch(match.Groups["pins"].Value,@"^(?:_\d+)+$|^_[A-Za-z]_\d+$"))c.Issues.Add(new("pins","未知管脚/门电路后缀"));
            if(config.ArchivedTypes.Contains(c.Type))c.Type="待确认";
            var suffix=c.RawReference.Split('/').Skip(1).Where(x=>x.Length>0).Select(x=>x.ToUpperInvariant()).ToArray();
            var normalized=new HashSet<string>();
            foreach(var s in suffix)
                if(config.ProjectSuffixAliases.TryGetValue(profile,out var suffixMap)&&suffixMap.TryGetValue(s,out var projectAlias))normalized.Add(projectAlias);
                else if(config.SuffixAliases.TryGetValue(s,out var alias)) normalized.Add(alias);
                else c.Issues.Add(new("suffix",$"未知后缀 /{s}"));
            bool no=config.UntestableSymbols.Contains(c.StatusSymbol.Trim(),StringComparer.OrdinalIgnoreCase), yes=config.TestableSymbols.Contains(c.StatusSymbol.Trim(),StringComparer.OrdinalIgnoreCase), na=config.NotApplicableSymbols.Contains(c.StatusSymbol.Trim());
            c.SourceState=no?TestState.Untestable:yes?TestState.Testable:na?TestState.NotApplicable:TestState.Unknown;c.State=c.SourceState;
            c.AppliedRuleIds.Add(no?"F-02":yes||na?"F-03":"F-04");
            if(c.State==TestState.Unknown) c.Issues.Add(new("status","空白或未知检测状态"));
            c.Value=config.TypeValues.GetValueOrDefault(c.Type,c.RawValue);
            if(special?.DisplayValue!=null&&!special.Review)c.Value=special.DisplayValue;
            if(c.Type==resistorType && valueOverrideEnabled && config.ProfileValueOverrides.TryGetValue(profile,out var overrides)){c.Value=overrides.GetValueOrDefault(c.RawValue,c.RawValue);c.AppliedRuleIds.Add("V-01");}
            if(yes||na){c.Issues.Clear();continue;}
            if(c.State==TestState.Untestable)
            {
                if(special?.Review==true){c.Value=c.RawValue;c.Issues.Add(new("classification","训练样表与源值/分类矛盾，待人工确认："+special.Source));continue;}
                if(c.Type=="待确认") c.Issues.Add(new("type","未知元件前缀"));
                if(string.IsNullOrWhiteSpace(c.RawValue)) c.Issues.Add(new("value","标准值为空"));
                var matches=rulesByType[c.Type].Where(r=>
                    (r.Prefixes.Length==0||r.Prefixes.Contains(prefix)) &&
                    (r.Suffix==null||normalized.Contains(r.Suffix)) && !r.ExcludeSuffixes.Any(normalized.Contains) && (!r.NoOtherSuffixes||normalized.Count==0) &&
                    (r.ExactValue==null||r.ExactValue==c.RawValue) && (r.Reference==null||r.Reference==c.Reference)&&(r.References.Length==0||r.References.Contains(c.Reference))).ToList();
                c.MatchedRules=matches.Select(r=>new RuleMatch(r.RuleId,r.Priority,r.Reason,r.Status,r.Source)).ToList();
                c.AppliedRuleIds.AddRange(matches.Select(r=>r.RuleId));
                // 测试结果已由Coverage确认。参考参数不触发重新确认可测性。
                var candidates=matches.Where(r=>!r.ReviewOnly&&r.Reason.Length>0).ToList();
                var rule=candidates.FirstOrDefault();
                if(rule==null||!config.IsReasonActive(rule.Reason)) c.Issues.Add(new("classification","原因待确定，可直接导出或自行选择原因"));
                else
                {
                    c.ReasonCode=rule.Reason;c.RuleId=rule.RuleId;
                    if(rule.DisplayValue!=null)c.Value=rule.DisplayValue;
                    if(candidates.Any(r=>r.Reason!=rule.Reason&&!rule.Supersedes.Contains(r.RuleId)))c.Issues.Add(new("rule_conflict","多个规则原因不同且没有已声明的优先关系"));
                }
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
        foreach(var group in a.Components.Where(c=>c.State==TestState.Untestable&&!string.IsNullOrWhiteSpace(c.Reference)).GroupBy(c=>c.Reference).Where(g=>g.Count()>1))
            foreach(var c in group) c.Issues.Add(new("duplicate",$"重复位号 {group.Key}，来源行 {string.Join(",",group.Select(x=>x.SourceRow))}"));
    }
    public static List<Component> BaseFilter(Analysis a,FilterOptions f)=>a.Components.Where(c=>(f.Type is "全部" or "待确认"||f.Type==c.Type)&&c.Value.Contains(f.Value,StringComparison.OrdinalIgnoreCase)&&(c.Reference.Contains(f.Reference,StringComparison.OrdinalIgnoreCase)||c.RawReference.Contains(f.Reference,StringComparison.OrdinalIgnoreCase))).ToList();
    public static List<Component> Filter(Analysis a,FilterOptions f,RuleConfig cfg)=>BaseFilter(a,f).Where(c=>(f.Type!="待确认"||c.Pending)&&(f.Status.HasValue?c.State==f.Status:!f.OnlyUntestable||c.State==TestState.Untestable)&&(!f.OnlyPendingCause||c.State==TestState.Untestable&&!cfg.IsReasonActive(c.OutputReasonCode))&&cfg.ReasonText(c.OutputReasonCode).Contains(f.Reason,StringComparison.OrdinalIgnoreCase)).ToList();
    public static List<BlindGroup> Group(IEnumerable<Component> items)=>items.GroupBy(c=>(c.State,c.GroupCategory,c.Value,ReasonCode:c.OutputReasonCode)).Select(g=>new BlindGroup(g.Key.Value,g.Key.ReasonCode,g.OrderBy(c=>c.SourceRow).ToList())).ToList();
    public static void Edit(Analysis a, Component c, string reference,string type,string value,TestState state,string reasonCode,string note,string justification,RuleConfig cfg,string? reviewer=null,string? programEvidence=null)
    {
        if(string.IsNullOrWhiteSpace(justification))throw new InvalidDataException("请填写修改理由");
        if(!Regex.IsMatch(reference,@"^[A-Za-z]+\d+$")&&!(reference=="PCB"&&c.RawReference.Trim().Equals("PCB",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("规范位号必须为字母前缀加数字，例如 R103");
        if(state is TestState.Unknown or TestState.Conflict)throw new InvalidDataException("请选择明确的可测或不可测状态");
        if(c.SourceState!=TestState.Untestable||state!=c.SourceState)throw new InvalidDataException("检测结果以Coverage为准，仅能为打X的元件选择原因");
        if(!cfg.KnownTypes.Contains(type))throw new InvalidDataException("请选择已有元件类型");
        if(state==TestState.Untestable && (string.IsNullOrWhiteSpace(value)||!cfg.IsReasonActive(reasonCode)||type=="待确认"))throw new InvalidDataException("不可测记录必须填写类型、标准值和有效原因");
        string before=JsonSerializer.Serialize(c,RuleConfig.JsonOptions);
        if(!c.Pending&&c.Reference==reference.ToUpperInvariant()&&c.Type==type&&c.Value==value&&c.ReasonCode==reasonCode&&c.Note==note)return;
        reviewer=string.IsNullOrWhiteSpace(reviewer)?Environment.UserName:reviewer.Trim();
        c.Reference=reference.ToUpperInvariant(); c.Type=type; c.Value=value; c.State=state; c.ReasonCode=state==TestState.Testable?"":reasonCode; c.Note=note;c.ManuallyReviewed=true;c.ProgramEvidence=programEvidence??c.ProgramEvidence;
        c.Issues.RemoveAll(i=>i.Blocking); CheckDuplicates(a);
        var time=DateTimeOffset.Now;
        a.Edits.Add(new(time,c.SourceRow,before,JsonSerializer.Serialize(c,RuleConfig.JsonOptions),justification,reviewer,cfg.Version,a.Inspection));
        a.Corrections[c.SourceRow]=new(time,c.SourceRow,c.Reference,c.Type,c.Value,c.ReasonCode,c.Note);
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
