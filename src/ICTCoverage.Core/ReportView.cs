namespace ICTCoverage.Core;

public sealed record ReportRow(string Sheet,int SourceRow,Dictionary<InspectionKind,Component> Results)
{
    public Component Anchor=>Results.GetValueOrDefault(InspectionKind.Missing)??Results.Values.First();
    public TestState State=>Results.Values.Any(c=>c.State==TestState.Untestable)?TestState.Untestable:Results.Values.Any(c=>c.State is TestState.Unknown or TestState.Conflict)?TestState.Unknown:Results.Values.All(c=>c.State==TestState.NotApplicable)?TestState.NotApplicable:TestState.Testable;
    public bool Pending=>Results.Values.Any(c=>c.Pending);
    public bool PendingCause(RuleConfig cfg)=>Results.Values.Any(c=>c.State==TestState.Untestable&&!cfg.IsReasonActive(c.OutputReasonCode));
    public string Symbol(InspectionKind kind)=>!Results.TryGetValue(kind,out var c)?"未提供":c.State switch{TestState.Testable=>"√",TestState.Untestable=>"Х",TestState.NotApplicable=>"∕",_=>"?"};
    public string Reasons(RuleConfig cfg)=>string.Join("；",Results.Where(p=>p.Value.State==TestState.Untestable).Select(p=>p.Key.Name()+"："+cfg.ReasonText(p.Value.OutputReasonCode))) is {Length:>0} value?value:"—";
}
public static class ReportView
{
    public static List<ReportRow> Scope(CoverageReport report,InspectionKind? kind,FilterOptions filter)
    {
        var analyses=report.Analyses.Where(p=>!kind.HasValue||p.Key==kind).ToArray();
        return analyses.SelectMany(p=>p.Value.Components.Select(c=>(Kind:p.Key,Item:c))).GroupBy(p=>(p.Item.Sheet,p.Item.SourceRow)).Select(g=>new ReportRow(g.Key.Sheet,g.Key.SourceRow,g.ToDictionary(p=>p.Kind,p=>p.Item))).OrderBy(r=>r.SourceRow)
            .Where(r=>(filter.Type=="全部"||filter.Type=="待确认"&&r.Pending||r.Anchor.Type==filter.Type)&&r.Anchor.Value.Contains(filter.Value,StringComparison.OrdinalIgnoreCase)&&(r.Anchor.Reference.Contains(filter.Reference,StringComparison.OrdinalIgnoreCase)||r.Anchor.RawReference.Contains(filter.Reference,StringComparison.OrdinalIgnoreCase))).ToList();
    }
    public static List<ReportRow> Filter(IEnumerable<ReportRow> scope,FilterOptions filter,RuleConfig cfg)=>scope.Where(r=>(filter.Status.HasValue?r.State==filter.Status:!filter.OnlyUntestable||r.State==TestState.Untestable)&&(!filter.OnlyPendingCause||r.PendingCause(cfg))&&r.Reasons(cfg).Contains(filter.Reason,StringComparison.OrdinalIgnoreCase)).ToList();
    public static ExportSelection FullBlindPoint(CoverageReport report,RuleConfig cfg)=>ReportWorkflow.SelectExport(report,InspectionKind.Missing,new(),cfg);
}
