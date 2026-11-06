using ICTCoverage.Core;
using ClosedXML.Excel;
using Xunit;

namespace ICTCoverage.Tests;
public sealed class SimpleWorkflowTests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    [Theory][InlineData("∕")][InlineData("/")][InlineData("／")]
    public void SlashIsNotApplicable(string symbol)
    {
        var a=new Analysis{Components=[new(){RawReference="Q7_2_3/NP",RawValue="Q",StatusSymbol=symbol,Yellow=true}]};new AnalysisEngine(Config()).Analyze(a,"");Assert.Equal(TestState.NotApplicable,a.Components[0].State);Assert.Equal(0,a.Pending);Assert.Equal(0,a.Untestable);
    }
    [Fact]public void NonXRecordsNeverTriggerSuffixOrProgramReview()
    {
        var a=new Analysis{Components=[new(){RawReference="U1/UNKNOWN",RawValue="",StatusSymbol="√",Yellow=true},new(){RawReference="Q7_2_3/NP",StatusSymbol="√",Yellow=true}]};new AnalysisEngine(Config()).Analyze(a,"");Assert.Equal(2,a.Testable);Assert.Equal(0,a.Pending);Assert.Empty(AnalysisEngine.Filter(a,new(),Config()));
    }
    [Fact]public void NewRulesCanBeEditedArchivedAndRestoredWithoutLosingMetadata()
    {
        var cfg=Config();var originalCount=cfg.Rules.Count;var input=new ClassificationRule{RuleId=RuleManagementService.NewId(cfg),Stage="原因分类",Types=["电阻"],Prefixes=["ZZ"],Suffix="SPECIAL",Reason="NO_POINT",Priority=80,Status="用户自定义",Source="工程师确认"};
        var created=RuleManagementService.Update(cfg,input,"WHAPPY","新增规则",true);Assert.Equal(originalCount,cfg.Rules.Count);Assert.Equal(originalCount+1,created.Rules.Count);Assert.Equal("电阻",created.PrefixTypes["ZZ"]);Assert.Single(created.RuleChanges);
        var a=new Analysis{Components=[new(){RawReference="ZZ1/SPECIAL",RawValue="22K",StatusSymbol="X"}]};new AnalysisEngine(created).Analyze(a,"");Assert.Equal(input.RuleId,a.Components[0].RuleId);Assert.False(a.Components[0].Pending);
        var archived=RuleManagementService.Archive(created,input.RuleId,true,"WHAPPY","暂不使用");Assert.True(archived.Rules.Single(r=>r.RuleId==input.RuleId).Archived);new AnalysisEngine(archived).Analyze(a,"");Assert.True(a.Components[0].Pending);
        var restored=RuleManagementService.Archive(archived,input.RuleId,false,"WHAPPY","恢复");new AnalysisEngine(restored).Analyze(a,"");Assert.False(a.Components[0].Pending);Assert.Equal(3,restored.RuleChanges.Count);
        Assert.Throws<InvalidDataException>(()=>RuleManagementService.Archive(cfg,"F-01",true,"WHAPPY","不可删除管线"));
        var edited=RuleManagementService.Clone(restored).Rules.Single(r=>r.RuleId==input.RuleId);edited.Reason="CUSTOM_REASON_TEST";var updated=RuleManagementService.Update(restored,edited,"WHAPPY","新增原因",false,"自定义原因");Assert.Equal("自定义原因",updated.Reasons[edited.Reason]);
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{RuleManagementService.Save(updated,path);Assert.Equal(4,RuleConfig.Load(path).RuleChanges.Count);}finally{File.Delete(path);}
    }
    [Fact]public void FixedTemplateExportRequiresNoExternalSampleDirectoryAndWritesOneFile()
    {
        var cfg=Config();var a=new Analysis{Components=[new(){RawReference="R1/NP",RawValue="22",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(a,"");var dir=Path.Combine(Path.GetTempPath(),"ICT-"+Guid.NewGuid());Directory.CreateDirectory(dir);string path=Path.Combine(dir,"盲点表.xlsx");
        try{new ExcelExporter(cfg).ExportFixed(a,path,new("新光板","新成品"));Assert.Single(Directory.GetFiles(dir));using var wb=new XLWorkbook(path);Assert.Single(wb.Worksheets);Assert.Equal("R1.",wb.Worksheets.First().Cell(9,2).GetString());Assert.Contains("新光板",wb.Worksheets.First().Cell(4,1).GetString());}finally{File.Delete(path);Directory.Delete(dir);}
    }
}
