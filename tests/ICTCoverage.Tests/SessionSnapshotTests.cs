using System.Text.Json;
using ICTCoverage.Core;
using Xunit;
namespace ICTCoverage.Tests;
public sealed class SessionSnapshotTests
{
    [Fact]public void CopySeparatesMutableStateWithoutSerializingEvidence()
    {
        var c=new Component{Reference="R1",Issues=[new("classification","待确定")],AppliedRuleIds=["F-02"],MatchedRules=[new("r",1,"NO_POINT","","来源")]};
        var original=new CoverageReport{Analyses={{InspectionKind.Missing,new(){Components=[c]}}}};var next=original.Copy();var copied=next.Analyses[InspectionKind.Missing].Components[0];
        copied.Reference="R2";copied.Issues.Clear();copied.AppliedRuleIds.Clear();copied.MatchedRules.Clear();Assert.Equal("R1",c.Reference);Assert.Single(c.Issues);Assert.Single(c.AppliedRuleIds);Assert.Single(c.MatchedRules);
    }
    [Fact]public void SavedSessionContainsFinalValuesAndRestoresWithoutRawReportPayload()
    {
        var cfg=RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));var a=new Analysis{Components=[new(){SourceRow=1,Sheet="S",RawReference="R1/NP",RawValue="1K",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(a,"");
        AnalysisEngine.Edit(a,a.Components[0],"R1","电阻","1K",TestState.Untestable,"NO_POINT","备注","人工确认",cfg);
        var report=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,a}}};string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            BatchReasonService.Save(report,path);var saved=JsonSerializer.Deserialize<CoverageReport>(File.ReadAllText(path),RuleConfig.JsonOptions)!;Assert.Empty(saved.Analyses[InspectionKind.Missing].Components);Assert.Empty(saved.Analyses[InspectionKind.Missing].Edits);Assert.Single(saved.Analyses[InspectionKind.Missing].Corrections);
            var fresh=new Analysis{Components=[new(){SourceRow=1,Sheet="S",RawReference="R1/NP",RawValue="1K",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(fresh,"");var reopened=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,fresh}}};ReportWorkflow.Restore(reopened,saved,cfg);Assert.Equal("备注",fresh.Components[0].Note);Assert.Single(ManualResults.Get(reopened,cfg));
        }
        finally{File.Delete(path);}
    }
}
