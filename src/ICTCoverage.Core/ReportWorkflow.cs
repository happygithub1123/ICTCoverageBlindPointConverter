namespace ICTCoverage.Core;

public record ExportSelection(Analysis Result,int BoardTotal,int ScopeTotal,double ScopeCoverage,string ScopeLabel);
public static class ReportWorkflow
{
    public static void Analyze(CoverageReport report,RuleConfig cfg)
    {
        report.ProjectProfile=ProjectIdentification.Detect(report,cfg);
        foreach(var analysis in report.Analyses.Values)new AnalysisEngine(cfg).Analyze(analysis,report.ProjectProfile);
    }
    public static ExportSelection SelectExport(CoverageReport report,InspectionKind kind,FilterOptions filter,RuleConfig cfg)
    {
        if(!report.Analyses.TryGetValue(kind,out var a))throw new InvalidDataException("报告未提供"+kind.Name()+"检测列");
        var scope=AnalysisEngine.BaseFilter(a,filter);var selected=AnalysisEngine.Filter(a,filter,cfg).Where(c=>c.State==TestState.Untestable).ToList();
        if(selected.Count==0)throw new InvalidOperationException("当前筛选结果没有Х不可测记录，不生成空文件");
        var export=new Analysis{Inspection=kind,SourcePath=a.SourcePath,SourceFingerprint=a.SourceFingerprint,BoardNumber=a.BoardNumber,ProductNumber=a.ProductNumber,Profile=a.Profile,RuleVersion=a.RuleVersion,Components=selected};
        var label=filter.Type+(filter.Reference.Length>0?" / 位号："+filter.Reference:"")+(filter.Value.Length>0?" / 标准值："+filter.Value:"")+(filter.Reason.Length>0?" / 原因："+filter.Reason:"");
        return new(export,a.Total,scope.Count,InspectionStatistics.From(scope).Coverage??0,label);
    }
    public static void Restore(CoverageReport fresh,CoverageReport saved,RuleConfig cfg)
    {
        if(saved.SourceFingerprint.Length>0&&saved.SourceFingerprint!=fresh.SourceFingerprint)throw new InvalidDataException("人工记录与当前报告内容不一致");
        foreach(var pair in saved.Analyses)
        {
            if(!fresh.Analyses.TryGetValue(pair.Key,out var current))continue;
            foreach(var correction in ManualResults.Latest(pair.Value))
            {
                var c=current.Components.FirstOrDefault(c=>c.SourceRow==correction.SourceRow);
                if(c==null||c.SourceState!=TestState.Untestable||!cfg.Reasons.ContainsKey(correction.ReasonCode))continue;
                if(!cfg.IsReasonActive(correction.ReasonCode)){current.Corrections[c.SourceRow]=correction;continue;}
                int count=current.Edits.Count;
                AnalysisEngine.Edit(current,c,correction.Reference,correction.Type,correction.Value,c.SourceState,correction.ReasonCode,correction.Note,"恢复已保存的原因选择",cfg);
                if(current.Edits.Count>count)current.Edits[^1]=current.Edits[^1] with{Time=correction.Time};
                current.Corrections[c.SourceRow]=correction;
            }
        }
    }
}
