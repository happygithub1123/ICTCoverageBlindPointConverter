using System.Text.Json;

namespace ICTCoverage.Core;

public static class Inspections
{
    public static string Name(this InspectionKind kind)=>kind switch{InspectionKind.Missing=>"缺件",InspectionKind.Short=>"短路",InspectionKind.Wrong=>"错件",_=>"空焊"};
    public static bool TryParse(string text,out InspectionKind kind)
    {
        var normalized=text.Trim().Replace("錯件","错件");
        foreach(var k in Enum.GetValues<InspectionKind>())if(normalized==k.Name()){kind=k;return true;}
        kind=default;return false;
    }
}
public sealed class CoverageReport
{
    public string SourcePath { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public Dictionary<InspectionKind,Analysis> Analyses { get; set; } = [];
    public string ProjectProfile { get; set; } = "";
}
public record InspectionStatistics(int Total,int Testable,int Untestable,int NotApplicable,int Unknown)
{
    public double? Coverage=>Total==0?null:(double)Testable/Total;
    public string Percentage=>Coverage.HasValue?Math.Round(Coverage.Value*100,0,MidpointRounding.AwayFromZero).ToString("F0")+"%":"—";
    public static InspectionStatistics From(IEnumerable<Component> source)
    {
        var all=source.ToList();return new(all.Count,all.Count(c=>c.SourceState==TestState.Testable),all.Count(c=>c.SourceState==TestState.Untestable),all.Count(c=>c.SourceState==TestState.NotApplicable),all.Count(c=>c.SourceState is TestState.Unknown or TestState.Conflict));
    }
}
public static class ProjectIdentification
{
    public static string Identity(Analysis analysis)=>string.IsNullOrWhiteSpace(analysis.BoardNumber)||string.IsNullOrWhiteSpace(analysis.ProductNumber)?"":analysis.BoardNumber+"-"+analysis.ProductNumber;
    public static string Detect(CoverageReport report,RuleConfig cfg)
    {
        string stem=Path.GetFileNameWithoutExtension(report.SourcePath),metadata=report.Analyses.Count>0?Identity(report.Analyses.Values.First()):"";
        var profiles=cfg.ProfileValueOverrides.Keys.Union(cfg.Rules.Where(r=>r.Profile!=null).Select(r=>r.Profile!)).ToArray();
        // 报告中的完整编号优先，避免改名后的文件误套其他板卡规则。
        if(metadata.Length>0)return profiles.FirstOrDefault(id=>metadata.Equals(id,StringComparison.OrdinalIgnoreCase))??"";
        return profiles.Where(id=>stem.Equals(id,StringComparison.OrdinalIgnoreCase)||stem.StartsWith(id+"-",StringComparison.OrdinalIgnoreCase)).OrderByDescending(id=>id.Length).FirstOrDefault()??"";
    }
}
public record ManualCorrection(DateTimeOffset Time,int SourceRow,string Reference,string Type,string Value,string ReasonCode,string Note);
public record FinalCorrection(string Reference,string Type,string Inspection,string Value,string Reason);
public static class ManualResults
{
    public static IReadOnlyList<ManualCorrection> Latest(Analysis analysis)
    {
        var one=new CoverageReport{Analyses={{analysis.Inspection,analysis}}};
        return Collect(one).Select(e=>e.Item).ToList();
    }
    public static IReadOnlyList<FinalCorrection> Get(CoverageReport report,RuleConfig cfg)
        =>Collect(report).OrderByDescending(e=>e.Time).Select(e=>new FinalCorrection(e.Item.Reference,e.Item.Type,e.Kind.Name(),e.Item.Value,cfg.ReasonText(e.Item.ReasonCode))).ToList();
    static List<(DateTimeOffset Time,InspectionKind Kind,ManualCorrection Item)> Collect(CoverageReport report)
    {
        var results=new List<(DateTimeOffset Time,InspectionKind Kind,ManualCorrection Item)>();
        foreach(var pair in report.Analyses)
        {
            var latest=new Dictionary<int,ManualCorrection>();
            foreach(var e in pair.Value.Edits.OrderBy(e=>e.Time))
            {
                try
                {
                    var after=JsonSerializer.Deserialize<Component>(e.After,RuleConfig.JsonOptions);var before=JsonSerializer.Deserialize<Component>(e.Before,RuleConfig.JsonOptions);
                    if(after==null||before==null||(!before.Pending&&Same(before,after)))continue;
                    latest[e.SourceRow]=new(e.Time,e.SourceRow,after.Reference,after.Type,after.Value,after.ReasonCode,after.Note);
                }catch(JsonException){/* 无法读取的历史数据不冒充有效修改。 */}
            }
            foreach(var e in pair.Value.Corrections.Values)if(!latest.TryGetValue(e.SourceRow,out var old)||old.Time<=e.Time)latest[e.SourceRow]=e;
            results.AddRange(latest.Values.Select(e=>(e.Time,pair.Key,e)));
        }
        return results;
    }
    internal static bool Same(Component a,Component b)=>a.Reference==b.Reference&&a.Type==b.Type&&a.Value==b.Value&&a.ReasonCode==b.ReasonCode&&a.Note==b.Note;
}
