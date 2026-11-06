using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICTCoverage.Core;

public enum TestState { Unknown, Testable, Untestable, Conflict, NotApplicable }
public enum InspectionKind { Missing, Short, Wrong, OpenSolder }
public record Issue(string Code, string Message, bool Blocking = true);
public record RuleMatch(string RuleId, int Priority, string ReasonCode, string Reliability, string Source);
public sealed class Component
{
    public int SourceRow { get; set; }
    public string Sheet { get; set; } = "";
    public string RawReference { get; set; } = "";
    public string Reference { get; set; } = "";
    public string RawValue { get; set; } = "";
    public string Value { get; set; } = "";
    public string StatusSymbol { get; set; } = "";
    public bool Yellow { get; set; }
    public string Type { get; set; } = "待确认";
    public TestState State { get; set; }
    public TestState SourceState { get; set; }
    public bool ManuallyReviewed { get; set; }
    // 仅兼容读取旧会话，不参与检测、归因或校验。
    public string ProgramEvidence { get; set; } = "";
    public List<RuleMatch> MatchedRules { get; set; } = [];
    public List<string> AppliedRuleIds { get; set; } = [];
    public string ReasonCode { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Note { get; set; } = "";
    public List<Issue> Issues { get; set; } = [];
    public bool Pending => Issues.Any(i => i.Blocking) || State is TestState.Unknown or TestState.Conflict;
    public bool HasDataIssues => State is TestState.Unknown or TestState.Conflict || Issues.Any(i=>i.Blocking&&i.Code is not ("classification" or "rule_conflict"));
    public string GroupCategory
    {
        get{string prefix=System.Text.RegularExpressions.Regex.Match(Reference,"^[A-Za-z]+").Value.ToUpperInvariant();return prefix=="TVS"?"TVS":Type=="待确认"?"待确认/"+prefix:Type;}
    }
    public string OutputReasonCode => Issues.Any(i=>i.Code=="rule_conflict")?"":ReasonCode;
}
public record FieldMap(string Sheet, int HeaderRow, int ReferenceColumn, int ValueColumn, int StatusColumn)
{
    public Dictionary<InspectionKind,int> InspectionColumns { get; init; } = [];
    public override string ToString() => $"{Sheet}：数据从 {HeaderRow + 2} 行，位号列 {ReferenceColumn + 1}，标准值列 {ValueColumn + 1}，ICT检测列：{(InspectionColumns.Count==0?(StatusColumn+1).ToString():string.Join("、",InspectionColumns.Select(p=>p.Key.Name()+" "+(p.Value+1))))}";
}
public record EditLog(DateTimeOffset Time, int SourceRow, string Before, string After, string Reason, string Reviewer = "", string RuleVersion = "", InspectionKind Inspection = InspectionKind.Missing);
public sealed class Analysis
{
    public InspectionKind Inspection { get; set; } = InspectionKind.Missing;
    public string SourcePath { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string Profile { get; set; } = "";
    public string BoardNumber { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public List<Component> Components { get; set; } = [];
    public List<EditLog> Edits { get; set; } = [];
    public Dictionary<int,ManualCorrection> Corrections { get; set; } = [];
    public int Total => Components.Count;
    public int Testable => Components.Count(c => c.SourceState == TestState.Testable);
    public int SourceUntestable => Components.Count(c => c.SourceState == TestState.Untestable);
    public int OutputTestable => Components.Count(c => c.State == TestState.Testable);
    public int Untestable => Components.Count(c => c.State == TestState.Untestable);
    public int Pending => Components.Count(c => c.Pending)+Issues.Count(i=>i.Blocking);
    public int DataIssues => Components.Count(c=>c.HasDataIssues)+Issues.Count(i=>i.Blocking);
    public double Coverage => Total == 0 ? 0 : (double)Testable / Total;
    public double OutputCoverage => Total == 0 ? 0 : (double)OutputTestable / Total;
    public double? ReportCoverage { get; set; }
    public int? ReportTestable { get; set; }
    public int? ReportUntestable { get; set; }
    public List<Issue> Issues { get; set; } = [];
    public string RuleVersion { get; set; } = "";
}
public sealed class ClassificationRule
{
    public InspectionKind[] Inspections { get; set; } = [InspectionKind.Missing];
    public string Id { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Stage { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = "";
    public string Scope { get; set; } = "report_profile";
    public string Source { get; set; } = "";
    public string[] LegacyIds { get; set; } = [];
    public string[] Supersedes { get; set; } = [];
    public bool ReviewOnly { get; set; }
    public bool BlockingReview { get; set; } = true;
    // 旧配置字段保留供来源追溯，当前流程只依据Coverage符号，不执行这些标记。
    public bool RequiresProgram { get; set; }
    public bool ReviewOutsideSample { get; set; }
    public bool NoOtherSuffixes { get; set; }
    public bool Archived { get; set; }
    public string[] Types { get; set; } = [];
    public string[] Prefixes { get; set; } = [];
    public string? Suffix { get; set; }
    public string[] ExcludeSuffixes { get; set; } = [];
    public string? ExactValue { get; set; }
    public string? Reference { get; set; }
    public string[] References { get; set; } = [];
    public string? DisplayValue { get; set; }
    public string? Profile { get; set; }
    [JsonPropertyName("reasonCode")] public string Reason { get; set; } = "";
    [JsonExtensionData] public Dictionary<string,JsonElement> Metadata { get; set; } = [];
}
public sealed class RuleConfig
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; } = "";
    public string SampleProfile { get; set; } = "MG10P0B3270350-P25086380101";
    public string CoverageDisplay { get; set; } = "truncate";
    [JsonExtensionData] public Dictionary<string,JsonElement> Metadata { get; set; } = [];
    public Dictionary<string,string> PrefixTypes { get; set; } = [];
    public Dictionary<string,Dictionary<string,string>> ProjectPrefixTypes { get; set; } = [];
    public Dictionary<string,Dictionary<string,string>> ProjectSuffixAliases { get; set; } = [];
    [JsonIgnore] public IEnumerable<string> KnownTypes=>PrefixTypes.Values.Concat(ProjectPrefixTypes.Values.SelectMany(m=>m.Values)).Distinct();
    public Dictionary<string,string> SuffixAliases { get; set; } = [];
    public string[] UntestableSymbols { get; set; } = ["X", "Х", "×"];
    public string[] TestableSymbols { get; set; } = ["√", "✓", "✔"];
    public string[] NotApplicableSymbols { get; set; } = ["∕", "/", "／", "—"];
    public Dictionary<string,string> Reasons { get; set; } = [];
    public HashSet<string> ArchivedReasons { get; set; } = [];
    public List<ReasonChange> ReasonChanges { get; set; } = [];
    [JsonIgnore] public IEnumerable<KeyValuePair<string,string>> ActiveReasons=>Reasons.Where(p=>!ArchivedReasons.Contains(p.Key));
    public Dictionary<string,string> TypeValues { get; set; } = [];
    public Dictionary<string,Dictionary<string,string>> ProfileValueOverrides { get; set; } = [];
    public List<ClassificationRule> Rules { get; set; } = [];
    public List<RuleChange> RuleChanges { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new JsonStringEnumConverter() } };
    public static RuleConfig Load(string path)
    {
        var c = JsonSerializer.Deserialize<RuleConfig>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("规则文件为空");
        c.Validate();
        return c;
    }
    public void Validate()
    {
        if(SchemaVersion!=2)throw new InvalidDataException("需要V2运行规则；旧规则或文档台账不能直接导入，原文件会保留。");
        if(PrefixTypes.Count==0||Reasons.Count==0||Rules.Count==0)throw new InvalidDataException("规则缺少前缀、原因或分类规则");
        if(Rules.Any(r=>string.IsNullOrWhiteSpace(r.RuleId)||string.IsNullOrWhiteSpace(r.Source))||Rules.Select(r=>r.RuleId).Distinct().Count()!=Rules.Count)throw new InvalidDataException("规则编号/来源为空或编号重复");
        if(Rules.Any(r=>r.Reason.Length>0&&!Reasons.ContainsKey(r.Reason)))throw new InvalidDataException("规则原因代码不存在");
        if(CoverageDisplay is not ("truncate" or "round"))throw new InvalidDataException("覆盖率显示只支持truncate或round");
        if(UntestableSymbols.Intersect(TestableSymbols,StringComparer.OrdinalIgnoreCase).Any())throw new InvalidDataException("可测和不可测符号重叠");
        if(NotApplicableSymbols.Intersect(UntestableSymbols.Concat(TestableSymbols),StringComparer.OrdinalIgnoreCase).Any())throw new InvalidDataException("不适用符号与检测状态重叠");
        if(Rules.Any(r=>r.Archived&&r.Enabled))throw new InvalidDataException("已删除规则不能同时启用");
        if(Rules.Any(r=>r.Inspections.Length==0||r.Inspections.Any(k=>!Enum.IsDefined(k))))throw new InvalidDataException("请选择有效的适用检测项目");
        if(Rules.Any(r=>r.Stage is not ("筛选" or "位号解析" or "标准值" or "原因分类" or "可测性扩展" or "异常审核" or "分组" or "导出" or "统计" or "回归测试")))throw new InvalidDataException("未知规则阶段");
        if(Rules.Any(r=>(r.Stage is "原因分类" or "可测性扩展")&&(r.Types.Length==0||r.Priority<0||r.Scope=="project"&&string.IsNullOrWhiteSpace(r.Profile))))throw new InvalidDataException("分类规则缺类型、有效优先级或项目范围");
        if(Rules.Any(r=>r.Stage=="原因分类"&&!r.ReviewOnly&&string.IsNullOrWhiteSpace(r.Reason)))throw new InvalidDataException("自动分类规则缺少reasonCode");
        if(Rules.Any(r=>r.Supersedes.Any(id=>!Rules.Any(other=>other.RuleId==id))))throw new InvalidDataException("优先关系引用不存在的规则编号");
        if(Metadata.TryGetValue("referenceParameters",out var parameters)&&parameters.EnumerateArray().Any(p=>p.TryGetProperty("computeEnabled",out var on)&&on.GetBoolean()))throw new InvalidDataException("10%/30%定义尚未确认，本版本不支持启用计算公式");
        foreach(var id in new[]{"F-01","F-02","F-03","F-04","N-01","N-02","V-02","V-03","E-01","G-01","G-02","O-01","O-02","O-03"})
            if(!Rules.Any(r=>r.RuleId==id&&r.Enabled))throw new InvalidDataException($"管线规则 {id} 必须启用；未知/损坏数据不能通过关闭审核绕过");
    }
    public string FormatCoverage(double ratio) => ((CoverageDisplay=="round"?Math.Round(ratio*100,2,MidpointRounding.AwayFromZero):Math.Floor(ratio*10000)/100)).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"%";
    public bool IsReasonActive(string code)=>Reasons.ContainsKey(code)&&!ArchivedReasons.Contains(code);
    public string ReasonText(string code) => IsReasonActive(code)?Reasons[code]:"待确定";
}
public record FilterOptions(string Type = "全部", string Value = "", string Reason = "", string Reference = "", bool OnlyUntestable = true, TestState? Status = null,bool OnlyPendingCause=false);
public sealed record BlindGroup(string Value, string ReasonCode, List<Component> Items)
{
    public int Count => Items.Count;
    public bool Pending => Items.Any(c=>c.Pending);
    public bool HasDataIssues => Items.Any(c=>c.HasDataIssues);
}
