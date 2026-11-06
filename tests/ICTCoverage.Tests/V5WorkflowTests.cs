using ICTCoverage.Core;
using ClosedXML.Excel;
using System.Text.Json;
using Xunit;
namespace ICTCoverage.Tests;

public sealed class FourProjectsFactAttribute:FactAttribute
{
    public FourProjectsFactAttribute(){string path=Path.Combine(ConverterTests.Root,"outputs","four-project-fixtures.json");if(!File.Exists(path)){Skip="四组用户原始文件不随公开源码分发";return;}using var d=JsonDocument.Parse(File.ReadAllText(path));if(d.RootElement.EnumerateArray().Any(p=>!File.Exists(p.GetProperty("source").GetString())))Skip="本机四组Coverage原文件缺失";}
}
public sealed class V5WorkflowTests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    static CoverageReport Read(string path){var reader=new CoverageReader();var snap=CoverageSnapshot.Read(path);var result=reader.ReadReport(snap,reader.Locate(snap).Single());ReportWorkflow.Analyze(result,Config());return result;}
    [FourProjectsFact]public void FourProjectsKeepAllSourceXAndKnownReasonsAndFlagContradictions()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(ConverterTests.Root,"outputs","four-project-fixtures.json")));var cfg=Config();
        foreach(var fixture in doc.RootElement.EnumerateArray())
        {
            var report=Read(fixture.GetProperty("source").GetString()!);Assert.Equal(fixture.GetProperty("project").GetString(),report.ProjectProfile);var a=report.Analyses[InspectionKind.Missing];Assert.Equal(fixture.GetProperty("total").GetInt32(),a.Total);Assert.Equal(fixture.GetProperty("x").GetInt32(),a.Untestable);Assert.Equal(a.Total,ReportView.Scope(report,null,new()).Count);
            foreach(var e in fixture.GetProperty("expected").EnumerateArray())
            {
                var c=a.Components.Single(c=>c.Reference==e.GetProperty("ref").GetString());Assert.Equal(e.GetProperty("type").GetString(),c.Type);Assert.Equal(e.GetProperty("value").GetString(),c.Value);Assert.Equal(e.GetProperty("reason").GetString(),cfg.ReasonText(c.OutputReasonCode).Replace("\n","").Replace("\r",""));Assert.False(c.HasDataIssues,c.RawReference+" "+string.Join(";",c.Issues.Select(i=>i.Message)));
            }
            foreach(var e in fixture.GetProperty("review").EnumerateArray()){var c=a.Components.Single(c=>c.Reference==e.GetString());Assert.True(c.Pending);Assert.Equal("",c.ReasonCode);Assert.Equal(c.RawValue,c.Value);Assert.False(c.HasDataIssues);}
            string dir=Path.Combine(ConverterTests.Root,"outputs","v0.5.0-四组回归");Directory.CreateDirectory(dir);string output=Path.Combine(dir,report.ProjectProfile+"-整板盲点表.xlsx");var selection=ReportView.FullBlindPoint(report,cfg);Assert.Equal(0,selection.Result.DataIssues);new ExcelExporter(cfg).ExportFixed(selection.Result,output,new(a.BoardNumber,a.ProductNumber,BoardTotal:a.Total,ScopeTotal:a.Total,ScopeCoverage:a.Coverage,ScopeLabel:"整板全部元件 · 缺件Х"));ExcelExporter.Verify(output,a);
            Assert.DoesNotContain(a.Components.Where(c=>c.State==TestState.Testable),c=>selection.Result.Components.Contains(c));Assert.All(AnalysisEngine.Group(selection.Result.Components),g=>Assert.Single(g.Items.Select(c=>c.GroupCategory).Distinct()));
        }
    }
    [Fact]public void AllViewUsesSourceRowsAndDefinedAggregateStates()
    {
        Component Make(int row,string symbol)=>new(){Sheet="S",SourceRow=row,RawReference="R"+row,RawValue="1K",StatusSymbol=symbol};var cfg=Config();var report=new CoverageReport{Analyses={{InspectionKind.Missing,new(){Components=[Make(1,"√"),Make(2,"√"),Make(3,"∕"),Make(4,"?"),Make(5,"√")]}},{InspectionKind.Short,new(){Inspection=InspectionKind.Short,Components=[Make(1,"Х"),Make(2,"∕"),Make(3,"∕"),Make(4,"√"),Make(5,"√")]}}}};ReportWorkflow.Analyze(report,cfg);
        var scope=ReportView.Scope(report,null,new());Assert.Equal(5,scope.Count);Assert.Equal(new[]{TestState.Untestable,TestState.Testable,TestState.NotApplicable,TestState.Unknown,TestState.Testable},scope.Select(r=>r.State));Assert.Equal("Х",scope[0].Symbol(InspectionKind.Short));Assert.Equal("未提供",scope[0].Symbol(InspectionKind.Wrong));Assert.Single(ReportView.Filter(scope,new(OnlyUntestable:false,Status:TestState.Untestable),cfg));Assert.Equal(2,ReportView.Filter(scope,new(OnlyUntestable:false,Status:TestState.Testable),cfg).Count);Assert.Contains(ReportView.Scope(report,null,new(Type:"待确认")),r=>r.SourceRow==1);
        Assert.Equal(5,ReportView.Scope(report,InspectionKind.Missing,new()).Count);Assert.Throws<InvalidOperationException>(()=>ReportView.FullBlindPoint(report,cfg));
    }
    [Fact]public void BatchReasonsAreAtomicAndPreserveSourceFieldsAndDataIssues()
    {
        var cfg=Config();var a=new Analysis{Inspection=InspectionKind.Short,SourceFingerprint="fp",Components=[new(){Sheet="S",SourceRow=1,RawReference="R1",RawValue="1K",StatusSymbol="X"},new(){Sheet="S",SourceRow=2,RawReference="R2",RawValue="2K",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(a,"");var report=new CoverageReport{SourceFingerprint="fp",Analyses={{InspectionKind.Short,a}}};var first=new CauseTarget(InspectionKind.Short,"S",1,"fp");var second=new CauseTarget(InspectionKind.Short,"S",2,"fp");
        Assert.Throws<InvalidDataException>(()=>BatchReasonService.Prepare(report,[first,second with{SourceRow=99}],"NO_POINT",cfg));Assert.Empty(a.Edits);Assert.Equal("",a.Components[0].ReasonCode);
        Assert.Throws<InvalidDataException>(()=>BatchReasonService.Prepare(report,[first with{Fingerprint="other"}],"NO_POINT",cfg));var next=BatchReasonService.Prepare(report,[first,second],"NO_POINT",cfg);Assert.Empty(a.Edits);Assert.Equal(2,ManualResults.Get(next,cfg).Count);Assert.Equal(a.Components.Select(c=>(c.RawReference,c.RawValue,c.SourceState)),next.Analyses[InspectionKind.Short].Components.Select(c=>(c.RawReference,c.RawValue,c.SourceState)));
        var unchanged=BatchReasonService.Prepare(next,[first,second],"NO_POINT",cfg);Assert.Equal(2,unchanged.Analyses[InspectionKind.Short].Edits.Count);Assert.Throws<DirectoryNotFoundException>(()=>BatchReasonService.Save(next,Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString(),"session.json")));Assert.Empty(a.Edits);
        a.Components[1].Issues.Add(new("duplicate","重复位号"));Assert.Throws<InvalidDataException>(()=>BatchReasonService.Prepare(report,[first,second],"NO_POINT",cfg));Assert.Contains(a.Components[1].Issues,i=>i.Code=="duplicate");var isolated=BatchReasonService.Prepare(report,[first],"NO_POINT",cfg);Assert.Contains(isolated.Analyses[InspectionKind.Short].Components[1].Issues,i=>i.Code=="duplicate");
    }
    [Fact]public void NewBaseRulesAndOldRuleScopesSurviveUpgrade()
    {
        var cfg=Config();Assert.Equal(326,cfg.Rules.Count);Assert.Equal(11,cfg.Rules.Count(r=>r.RuleId.StartsWith("BASE5-")));Assert.All(cfg.Rules.Where(r=>r.RuleId.StartsWith("BASE5-")),r=>Assert.Equal(4,r.Inspections.Length));Assert.Equal(InspectionKind.Missing,Assert.Single(cfg.Rules.Single(r=>r.RuleId=="R-02").Inspections));
        var old=RuleManagementService.Clone(cfg);old.Rules.RemoveAll(r=>r.RuleId.StartsWith("BASE5-")||r.RuleId.StartsWith("LEARN-5-"));old.ComponentOverrides.Clear();old.Version="2.2.1";var next=RuleManagementService.Upgrade(old,cfg);Assert.Equal(326,next.Rules.Count);Assert.NotEmpty(next.ComponentOverrides);Assert.Equal(InspectionKind.Missing,Assert.Single(next.Rules.Single(r=>r.RuleId=="R-02").Inspections));
    }
    [Fact]public void LongGroupsContinueWithoutShrinkingFontOrLosingReferences()
    {
        var cfg=Config();var a=new Analysis{Components=Enumerable.Range(1,400).Select(i=>new Component{SourceRow=i,RawReference="RA"+i+"/NP",RawValue="1K",StatusSymbol="X"}).ToList()};new AnalysisEngine(cfg).Analyze(a,"");Assert.Single(AnalysisEngine.Group(a.Components));string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try{new ExcelExporter(cfg).ExportFixed(a,path,new("",""));ExcelExporter.Verify(path,a);using var wb=new XLWorkbook(path);var(s,start,count)=ExcelExporter.LocateTemplate(wb);Assert.True(count>1);Assert.Equal(400,Enumerable.Range(start,count).Sum(r=>s.Cell(r,3).GetValue<int>()));Assert.All(Enumerable.Range(start,count),r=>{Assert.True(s.Row(r).Height<=409);Assert.Equal(12,s.Cell(r,2).Style.Font.FontSize);});}finally{File.Delete(path);}
    }
}
