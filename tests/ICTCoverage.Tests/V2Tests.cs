using Xunit;
using ICTCoverage.Core;
using ClosedXML.Excel;
using System.Text.Json;

namespace ICTCoverage.Tests;

public sealed class V2Tests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    static Analysis Analyze(string reference,string value="1",string status="Х",string profile="")
    {
        var a=new Analysis{Components=[new(){SourceRow=20,RawReference=reference,RawValue=value,StatusSymbol=status}]};new AnalysisEngine(Config()).Analyze(a,profile);return a;
    }
    [Fact]public void Catalog_All34RulesAndLegacyIdsArePreserved()
    {
        var cfg=Config();Assert.Equal(34,cfg.Rules.Count);Assert.Equal(34,cfg.Rules.Select(r=>r.RuleId).Distinct().Count());Assert.All(cfg.Rules,r=>Assert.NotEmpty(r.Source));
        Assert.Equal(2,cfg.SchemaVersion);Assert.Equal(105,cfg.Rules.Single(r=>r.RuleId=="C-02").Priority);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{File.WriteAllText(path,JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions));var reload=RuleConfig.Load(path);Assert.Equal(cfg.Rules[0].Metadata.Count,reload.Rules[0].Metadata.Count);Assert.Equal(cfg.Metadata.Count,reload.Metadata.Count);}finally{File.Delete(path);}
    }
    [Theory][InlineData("R1/NP","",TestState.Unknown)][InlineData("R1/NP","√",TestState.Testable)][InlineData("R1","✕",TestState.Untestable)][InlineData("R1"," x ",TestState.Untestable)]
    public void SourceColumnIsAuthoritative(string reference,string symbol,TestState state)
    {var a=Analyze(reference,status:symbol);Assert.Equal(state,a.Components[0].SourceState);Assert.Equal(state,a.Components[0].State);if(reference.Contains("NP")&&symbol=="√")Assert.False(a.Components[0].Pending);}
    [Fact]public void SourceDecisionsAreReadOnlyAndManualEditsOnlyChooseReasons()
    {
        var a=Analyze("Q7_2_3/NP",status:"√");var c=a.Components[0];Assert.Equal(1,a.Testable);Assert.False(c.Pending);
        Assert.Throws<InvalidDataException>(()=>AnalysisEngine.Edit(a,c,"Q7","三极管","Q",TestState.Untestable,"DQ","","不能改判",Config()));
        a=Analyze("C1/SG","1uF");c=a.Components[0];Assert.True(c.Pending);AnalysisEngine.Edit(a,c,"C1","电容","1uF",TestState.Untestable,"SHORT_GROUP","","手工选择原因",Config(),"王开心");Assert.False(c.Pending);Assert.Equal("王开心",a.Edits.Single().Reviewer);Assert.Equal(1,a.SourceUntestable);
    }
    [Theory][InlineData("D1/NP","D-01")][InlineData("Q1/NP","Q-01")][InlineData("U1/NP","U-01")]
    public void ProgramReasonsRequireReview(string reference,string rule)
    {var a=Analyze(reference);var c=a.Components[0];Assert.False(c.Pending);Assert.Equal(rule,c.RuleId);Assert.DoesNotContain(c.Issues,i=>i.Code=="program");Assert.Equal(rule,c.MatchedRules.Single().RuleId);}
    [Fact]public void ProgramEvidenceIsTraceableButDoesNotChangeSourceState()
    {
        var a=new Analysis{Components=[new(){RawReference="Q1/NP",RawValue="Q",StatusSymbol="√",ProgramEvidence="程序有Q1测试项"}]};new AnalysisEngine(Config()).Analyze(a,"");Assert.Equal(1,a.Testable);Assert.False(a.Components[0].Pending);
    }
    [Fact]public void ParallelResistorKeepsNPClassificationAndReviewEvidence()
    {var c=Analyze("R382/NP/R","22.00K").Components[0];Assert.Equal("R-02",c.RuleId);Assert.Equal("NO_POINT",c.ReasonCode);Assert.False(c.Pending);Assert.Contains(c.MatchedRules,r=>r.RuleId=="R-03");}
    [Fact]public void ShortGroupDoesNotInventEvidenceOrFormula()
    {
        var c=Analyze("C1/SG","1uF").Components[0];Assert.True(c.Pending);Assert.Equal("",c.ReasonCode);Assert.Contains(c.MatchedRules,r=>r.RuleId=="C-04");
        var p=Config().Metadata["referenceParameters"];foreach(var entry in p.EnumerateArray()){Assert.False(entry.GetProperty("computeEnabled").GetBoolean());Assert.Equal(JsonValueKind.Null,entry.GetProperty("formula").ValueKind);}
    }
    [Fact]public void CapacitorThirtyPercentIsReferenceOnlyAndPrecedenceIsExplicit()
    {
        var c=Analyze("C1/NP/C","1uF").Components[0];Assert.Equal("C-05",c.RuleId);Assert.Equal("NO_POINT",c.ReasonCode);Assert.False(c.Pending);Assert.Contains(c.MatchedRules,i=>i.RuleId=="C-07");
        c=Analyze("C199/C/PCB","0.100uF").Components[0];Assert.Equal("C-01",c.RuleId);Assert.False(c.Pending);
    }
    [Fact]public void ExplicitPriorityWinsIndependentOfArrayOrderAndUnknownConflictsBlock()
    {
        var cfg=Config();cfg.Rules.Reverse();var a=new Analysis{Components=[new(){RawReference="C1/NP",RawValue="10.00pF",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(a,cfg.SampleProfile);Assert.Equal("C-02",a.Components[0].RuleId);Assert.False(a.Components[0].Pending);
        cfg.Rules.Single(r=>r.RuleId=="C-02").Supersedes=[];new AnalysisEngine(cfg).Analyze(a,cfg.SampleProfile);Assert.True(a.Components[0].Pending);Assert.Contains(a.Components[0].Issues,i=>i.Code=="rule_conflict");
    }
    [Fact]public void SampleFallbackAndSpecialsAreOffForOtherProjects()
    {
        var c=Analyze("R1").Components[0];Assert.True(c.Pending);Assert.Equal("",c.ReasonCode);
        c=Analyze("R1",profile:Config().SampleProfile).Components[0];Assert.Equal("R-04",c.RuleId);
        c=Analyze("X1").Components[0];Assert.Equal("晶振",c.Type);Assert.Equal("晶振",c.Value);Assert.False(c.Pending);
    }
    [Fact]public void SourceSummaryMismatchBlocksFormalExport()
    {
        var a=Analyze("R1/NP");a.ReportUntestable=2;new AnalysisEngine(Config()).Analyze(a,"");Assert.Contains(a.Issues,i=>i.Code=="source_count");Assert.True(a.Pending>0);
    }
    [Fact]public void CoveragePolicyAndInvalidRulesAreChecked()
    {
        var cfg=Config();Assert.Equal("16.80%",cfg.FormatCoverage(202d/1202));cfg.CoverageDisplay="round";Assert.Equal("16.81%",cfg.FormatCoverage(202d/1202));cfg.Rules[1].RuleId=cfg.Rules[0].RuleId;Assert.Throws<InvalidDataException>(()=>cfg.Validate());
    }
    [Fact]public void SourceMetadataIsReadAndRetainedInAudit()
    {
        var reader=new CoverageReader();var path=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();var a=reader.Read(path,reader.Locate(path).Single());new AnalysisEngine(Config()).Analyze(a,Config().SampleProfile);
        Assert.Equal(202,a.ReportTestable);Assert.Equal(1000,a.ReportUntestable);Assert.Equal(202,a.Testable);Assert.Empty(a.Issues);Assert.Equal("P25086380101",a.ProductNumber);
        var output=Path.Combine(ConverterTests.Root,"outputs","v2-核对报告.xlsx");new ExcelExporter(Config()).ExportAudit(a,output);
        using var wb=new XLWorkbook(output);Assert.Equal(202,wb.Worksheet("统计").Cell(2,2).GetValue<int>());Assert.Contains("来源",wb.Worksheet("逐点核对").Cell(1,15).GetString());
    }
    [Fact]public void FullReviewFlow_EnablesFormalExportWithoutLosingSourceOrRules()
    {
        var cfg=Config();var reader=new CoverageReader();var source=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();var a=reader.Read(source,reader.Locate(source).Single());new AnalysisEngine(cfg).Analyze(a,cfg.SampleProfile);
        Assert.Equal(0,a.Pending);Assert.Equal(1000,a.Untestable);Assert.Equal(99,AnalysisEngine.Group(a.Components.Where(c=>c.State==TestState.Untestable)).Count);
        foreach(var c in a.Components.Where(c=>c.Pending).ToArray())AnalysisEngine.Edit(a,c,c.Reference,c.Type,c.Value,c.State,c.ReasonCode,c.Note,"自动化测试专用确认，不代表真实审核",cfg,"测试夹具");
        Assert.Equal(0,a.Pending);Assert.Equal(202,a.Testable);Assert.Equal(1000,a.SourceUntestable);Assert.Empty(a.Edits);Assert.NotEmpty(a.Components.Single(c=>c.Reference=="R382").MatchedRules);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");try{var template=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xlsx").Single();new ExcelExporter(cfg).Export(a,template,path,new(a.BoardNumber,a.ProductNumber));ExcelExporter.Verify(path,a);using var wb=new XLWorkbook(path);Assert.Contains("16.80%",wb.Worksheets.First().Cell(6,1).GetString());Assert.Contains("未提供",wb.Worksheets.First().Cell(6,1).GetString());}finally{File.Delete(path);}
    }
    [Fact]public void LegacyConfigIsNotSilentlyLoadedAsV2()
    {Assert.Throws<InvalidDataException>(()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"docs","baseline","legacy-runtime-rules.json")));}
}
