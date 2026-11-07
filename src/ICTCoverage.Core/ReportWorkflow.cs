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
        var label=filter.Type+(filter.Reference.Length>0?" / 位号："+filter.Reference:"")+(filter.Value.Length>0?" / 标准值："+filter.Value:"")+(filter.Reason.Length>0?" / 原因："+filter.Reason:"")+(filter.OnlyPendingCause?" / 原因待选择":"");
        return new(export,a.Total,scope.Count,InspectionStatistics.From(scope).Coverage??0,label);
    }
    public static void Restore(CoverageReport fresh,CoverageReport saved,RuleConfig cfg)
    {
        if(saved.SourceFingerprint.Length>0&&saved.SourceFingerprint!=fresh.SourceFingerprint)throw new InvalidDataException("人工记录与当前报告内容不一致");
        foreach(var pair in saved.Analyses)
        {
            if(!fresh.Analyses.TryGetValue(pair.Key,out var current))continue;
            var byRow=current.Components.ToLookup(c=>c.SourceRow);var knownTypes=cfg.KnownTypes.ToHashSet();
            foreach(var correction in ManualResults.Latest(pair.Value))
            {
                var c=byRow[correction.SourceRow].FirstOrDefault();
                if(c==null||c.SourceState!=TestState.Untestable||!cfg.Reasons.ContainsKey(correction.ReasonCode))continue;
                if(!cfg.IsReasonActive(correction.ReasonCode)){current.Corrections[c.SourceRow]=correction;continue;}
                string type=TypeManagementService.ResolveName(cfg,correction.Type);
                if(!knownTypes.Contains(type)){current.Corrections[c.SourceRow]=correction;continue;}
                if(string.IsNullOrWhiteSpace(correction.Value)||!System.Text.RegularExpressions.Regex.IsMatch(correction.Reference,@"^[A-Za-z]+\d+$|^PCB$"))throw new InvalidDataException("保存的人工记录包含无效位号或标准值");
                c.Reference=correction.Reference;c.Type=type;c.Value=correction.Value;c.ReasonCode=correction.ReasonCode;c.Note=correction.Note;c.ManuallyReviewed=true;
                c.Issues.RemoveAll(i=>i.Blocking);current.Corrections[c.SourceRow]=correction with{Type=type};
            }
            AnalysisEngine.CheckDuplicates(current);
        }
    }
}
