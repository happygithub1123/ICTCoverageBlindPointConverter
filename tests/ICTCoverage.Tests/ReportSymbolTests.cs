using ICTCoverage.Core;
using ClosedXML.Excel;
using System.Text.Json;
using Xunit;

namespace ICTCoverage.Tests;

public sealed class ReportSymbolTests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    public static IEnumerable<object[]> Cases()
    {
        foreach(string prefix in new[]{"D","Q","U"})foreach(string symbol in new[]{"X","Х","√","∕"})foreach(bool yellow in new[]{false,true})foreach(bool np in new[]{false,true})yield return [prefix,symbol,yellow,np];
    }
    [Theory][MemberData(nameof(Cases))]
    public void DqIcUseOnlyReportSymbols(string prefix,string symbol,bool yellow,bool np)
    {
        var cfg=Config();foreach(var rule in cfg.Rules){rule.RequiresProgram=true;rule.ReviewOutsideSample=true;rule.Status="样例已验证";} // 旧字段和旧状态不应形成额外限制。
        var a=new Analysis{Components=[new(){SourceRow=20,RawReference=prefix+"7"+(np?"/NP":""),RawValue="1",StatusSymbol=symbol,Yellow=yellow,ProgramEvidence=""}]};new AnalysisEngine(cfg).Analyze(a,"");var item=a.Components.Single();
        var expected=symbol is "X" or "Х"?TestState.Untestable:symbol=="√"?TestState.Testable:TestState.NotApplicable;
        Assert.Equal(expected,item.SourceState);Assert.Equal(expected,item.State);Assert.Equal(0,a.Pending);Assert.Empty(item.Issues);
        Assert.Equal(expected==TestState.Untestable?1:0,AnalysisEngine.Filter(a,new(),cfg).Count);
        if(expected==TestState.Untestable){Assert.Equal(prefix=="U"?"IC":"DQ",item.ReasonCode);Assert.False(string.IsNullOrWhiteSpace(item.RuleId));}else Assert.Equal("",item.ReasonCode);
    }
    [Fact]public void LegacyFieldsAndCustomRulesRoundTripWithoutValidationOrDataLoss()
    {
        var cfg=Config();var legacy=cfg.Rules.Single(r=>r.RuleId=="D-01");legacy.RequiresProgram=true;legacy.ReviewOutsideSample=true;legacy.Status="样例已验证";legacy.Metadata["customLegacyField"]=JsonSerializer.SerializeToElement(new{source="旧用户配置",keep=true});
        var input=new ClassificationRule{RuleId="CUSTOM-LEGACY",Stage="原因分类",Types=["三极管"],Prefixes=["Q"],Reference="Q999",Reason="DQ",Priority=99,Source="用户原有规则",Status="用户自定义"};cfg=RuleManagementService.Update(cfg,input,"工程师","旧规则兼容测试",true);
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            RuleManagementService.Save(cfg,path);var loaded=RuleConfig.Load(path);var old=loaded.Rules.Single(r=>r.RuleId=="D-01");Assert.True(old.RequiresProgram);Assert.True(old.Metadata["customLegacyField"].GetProperty("keep").GetBoolean());
            Assert.Equal("用户原有规则",loaded.Rules.Single(r=>r.RuleId=="CUSTOM-LEGACY").Source);var a=new Analysis{Components=[new(){RawReference="Q999/NP",RawValue="Q",StatusSymbol="Х",Yellow=true}]};new AnalysisEngine(loaded).Analyze(a,"");Assert.Equal("CUSTOM-LEGACY",a.Components[0].RuleId);Assert.Equal(0,a.Pending);
            var archived=RuleManagementService.Archive(loaded,input.RuleId,true,"工程师","停用");var restored=RuleManagementService.Archive(archived,input.RuleId,false,"工程师","恢复");Assert.True(restored.Rules.Single(r=>r.RuleId==input.RuleId).Enabled);Assert.Equal(3,restored.RuleChanges.Count);
        }finally{File.Delete(path);}
    }
    [Fact]public void CurrentExecutionAndHistoricalEvidenceAreClearlySeparated()
    {
        var cfg=Config();foreach(string id in new[]{"D-01","Q-01","U-01"})
        {
            var rule=cfg.Rules.Single(r=>r.RuleId==id);Assert.Contains("Coverage 打 X",RulePresentation.CurrentExecution(rule));Assert.DoesNotContain("复核",RulePresentation.CurrentExecution(rule));Assert.StartsWith("历史：",RulePresentation.HistoricalBasis(rule));Assert.Contains("originalAction",rule.Metadata["historicalEvidence"].GetRawText());
            rule.RequiresProgram=true;rule.Status="需要查看程序";Assert.DoesNotContain("程序",RulePresentation.CurrentExecution(rule));
        }
    }
    [Fact]public void UserExtensionMetadataCannotBreakRuleOverview()
    {
        var rule=Config().Rules.Single(r=>r.RuleId=="D-01");rule.Metadata["historicalEvidence"]=JsonSerializer.SerializeToElement("用户早期自定义资料");Assert.StartsWith("历史：",RulePresentation.HistoricalBasis(rule));Assert.Contains("Coverage",RulePresentation.CurrentExecution(rule));
    }
    [Fact]public void DqIcExportContainsOnlyXAndCreatesOnlyOneWorkbook()
    {
        var cfg=Config();var a=new Analysis();int row=20;
        foreach(string prefix in new[]{"D","Q","U"})foreach(string symbol in new[]{"Х","√","∕"})a.Components.Add(new(){SourceRow=row,RawReference=prefix+(row++)+"/NP",RawValue="1",StatusSymbol=symbol,Yellow=true});
        new AnalysisEngine(cfg).Analyze(a,"");Assert.Equal(3,a.Untestable);Assert.Equal(0,a.Pending);string dir=Path.Combine(Path.GetTempPath(),"ICT-symbols-"+Guid.NewGuid());Directory.CreateDirectory(dir);string file=Path.Combine(dir,"盲点表.xlsx");
        try{new ExcelExporter(cfg).ExportFixed(a,file,new("",""));Assert.Single(Directory.GetFiles(dir));using var wb=new XLWorkbook(file);Assert.Single(wb.Worksheets);var(s,start,count)=ExcelExporter.LocateTemplate(wb);var written=Enumerable.Range(start,count).SelectMany(r=>ExcelExporter.SplitRefs(s.Cell(r,2).GetString())).Order().ToArray();Assert.Equal(a.Components.Where(c=>c.SourceState==TestState.Untestable).Select(c=>c.Reference).Order().ToArray(),written);}finally{File.Delete(file);Directory.Delete(dir);}
    }
}
