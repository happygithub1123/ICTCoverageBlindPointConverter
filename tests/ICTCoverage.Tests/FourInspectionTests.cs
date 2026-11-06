using ICTCoverage.Core;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.Util;
using System.Text.Json;
using Xunit;

namespace ICTCoverage.Tests;

public sealed class FourInspectionTests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    static CoverageReport Sample()
    {
        var reader=new CoverageReader();var snap=CoverageSnapshot.Read(Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single());var report=reader.ReadReport(snap,reader.Locate(snap).Single());ReportWorkflow.Analyze(report,Config());return report;
    }
    [Fact]public void AllFourColumnsAndTypeStatisticsMatchSource()
    {
        var report=Sample();Assert.Equal(4,report.Analyses.Count);int[] yes=[202,381,202,202],no=[1000,821,1000,1000];
        foreach(var pair in report.Analyses){var stats=InspectionStatistics.From(pair.Value.Components);Assert.Equal(1202,stats.Total);Assert.Equal(yes[(int)pair.Key],stats.Testable);Assert.Equal(no[(int)pair.Key],stats.Untestable);Assert.Equal(pair.Key==InspectionKind.Short?"32%":"17%",stats.Percentage);}
        var missing=InspectionStatistics.From(AnalysisEngine.BaseFilter(report.Analyses[InspectionKind.Missing],new(Type:"电阻")));var shorted=InspectionStatistics.From(AnalysisEngine.BaseFilter(report.Analyses[InspectionKind.Short],new(Type:"电阻")));
        Assert.Equal((550,88,462),(missing.Total,missing.Testable,missing.Untestable));Assert.Equal((550,67,483),(shorted.Total,shorted.Testable,shorted.Untestable));Assert.Equal("12%",shorted.Percentage);
        Assert.Equal(0,report.Analyses[InspectionKind.Missing].Pending);Assert.True(report.Analyses[InspectionKind.Short].Pending>0);
        Assert.Equal("MG10P0B3270350-P25086380101",report.ProjectProfile);
    }
    [Fact]public void StatusAndReasonFiltersDoNotChangeStatisticsDenominator()
    {
        var a=Sample().Analyses[InspectionKind.Short];var cfg=Config();var f=new FilterOptions(Type:"电阻",OnlyUntestable:false,Status:TestState.Testable);Assert.Equal(67,AnalysisEngine.Filter(a,f,cfg).Count);Assert.Equal(550,AnalysisEngine.BaseFilter(a,f).Count);
        f=f with{Status=TestState.Untestable,Reason="不可能匹配"};Assert.Empty(AnalysisEngine.Filter(a,f,cfg));Assert.Equal(550,InspectionStatistics.From(AnalysisEngine.BaseFilter(a,f)).Total);Assert.Equal("—",InspectionStatistics.From([]).Percentage);
    }
    [Fact]public void MovedColumnsMissingColumnAndAoiColumnsAreHandled()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xls");
        try
        {
            using(var wb=new HSSFWorkbook()){var s=wb.CreateSheet("测试");var station=s.CreateRow(1);station.CreateCell(5).SetCellValue("AOI");station.CreateCell(6).SetCellValue("ICT");s.AddMergedRegion(new CellRangeAddress(1,1,6,14));var header=s.CreateRow(2);header.CreateCell(4).SetCellValue("标准值");header.CreateCell(5).SetCellValue("缺件");header.CreateCell(8).SetCellValue("短路");header.CreateCell(14).SetCellValue("错件");var data=s.CreateRow(3);data.CreateCell(1).SetCellValue("R1/NP");data.CreateCell(4).SetCellValue("22K");data.CreateCell(5).SetCellValue("X");data.CreateCell(8).SetCellValue("√");data.CreateCell(14).SetCellValue("∕");using var stream=File.Create(path);wb.Write(stream);}
            var reader=new CoverageReader();var snap=CoverageSnapshot.Read(path);var fields=reader.Locate(snap).Single();Assert.Equal(8,fields.InspectionColumns[InspectionKind.Short]);Assert.False(fields.InspectionColumns.ContainsKey(InspectionKind.Missing));var report=reader.ReadReport(snap,fields);ReportWorkflow.Analyze(report,Config());Assert.Equal(2,report.Analyses.Count);Assert.Equal(1,report.Analyses[InspectionKind.Short].Testable);Assert.Equal(TestState.NotApplicable,report.Analyses[InspectionKind.Wrong].Components.Single().State);
        }finally{File.Delete(path);}
    }
    static Analysis Editable(InspectionKind kind)
    {
        var a=new Analysis{Inspection=kind,SourceFingerprint="fixture",Components=[new(){SourceRow=20,RawReference="C1/SG",RawValue="1uF",StatusSymbol="X"}]};new AnalysisEngine(Config()).Analyze(a,"");return a;
    }
    [Fact]public void ManualResultsShowOnlyFinalChangesPerInspectionAndRestore()
    {
        var cfg=Config();var a=Editable(InspectionKind.Missing);var c=a.Components.Single();var report=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,a},{InspectionKind.Short,Editable(InspectionKind.Short)}}};Assert.Empty(ManualResults.Get(report,cfg));
        AnalysisEngine.Edit(a,c,"C1","电容","1uF",c.SourceState,"SHORT_GROUP","","选择原因",cfg);Assert.Single(ManualResults.Get(report,cfg));int count=a.Edits.Count;
        AnalysisEngine.Edit(a,c,"C1","电容","1uF",c.SourceState,"SHORT_GROUP","","原样保存",cfg);Assert.Equal(count,a.Edits.Count);
        AnalysisEngine.Edit(a,c,"C1","电容","1uF",c.SourceState,"PARALLEL","","再次校准",cfg);Assert.Equal(cfg.ReasonText("PARALLEL"),ManualResults.Get(report,cfg).Single().Reason);
        var b=report.Analyses[InspectionKind.Short];var d=b.Components.Single();AnalysisEngine.Edit(b,d,"C1","电容","1uF",d.SourceState,"NO_POINT","","短路原因",cfg);Assert.Equal(2,ManualResults.Get(report,cfg).Count);
        var saved=JsonSerializer.Deserialize<CoverageReport>(JsonSerializer.Serialize(report,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;var fresh=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,Editable(InspectionKind.Missing)},{InspectionKind.Short,Editable(InspectionKind.Short)}}};ReportWorkflow.Restore(fresh,saved,cfg);Assert.Equal(2,ManualResults.Get(fresh,cfg).Count);Assert.Equal("PARALLEL",fresh.Analyses[InspectionKind.Missing].Components[0].ReasonCode);Assert.Equal("NO_POINT",fresh.Analyses[InspectionKind.Short].Components[0].ReasonCode);
        // 旧会话只有Edits，仍转换成每个元件的最后结果。
        saved.Analyses[InspectionKind.Missing].Corrections.Clear();Assert.Equal(cfg.ReasonText("PARALLEL"),ManualResults.Get(saved,cfg).Single(r=>r.Inspection=="缺件").Reason);
    }
    [Fact]public void RulesAreInspectionScopedAndOldCustomRulesRemainMissingOnly()
    {
        var cfg=Config();var rule=cfg.Rules.Single(r=>r.RuleId=="C-04");Assert.Equal(InspectionKind.Missing,Assert.Single(rule.Inspections));
        var custom=new ClassificationRule{RuleId="CUSTOM-SHORT",Stage="原因分类",Source="测试",Types=["电容"],Reason="SHORT_GROUP",Inspections=[InspectionKind.Short],Suffix="SG",Priority=20};cfg=RuleManagementService.Update(cfg,custom,"测试","仅短路规则",true);
        var missing=Editable(InspectionKind.Missing);var shorted=Editable(InspectionKind.Short);new AnalysisEngine(cfg).Analyze(missing,"");new AnalysisEngine(cfg).Analyze(shorted,"");Assert.True(missing.Pending>0);Assert.Equal(0,shorted.Pending);
        string old="{\"ruleId\":\"OLD\",\"stage\":\"原因分类\"}";Assert.Equal(InspectionKind.Missing,Assert.Single(JsonSerializer.Deserialize<ClassificationRule>(old,RuleConfig.JsonOptions)!.Inspections));
    }
    [Fact]public void ExportUsesFilteredXOnlyAndCorrectScopeWithoutCompanionFiles()
    {
        var report=Sample();var cfg=Config();var f=new FilterOptions(Type:"电阻",OnlyUntestable:false,Status:TestState.Untestable);var selected=ReportWorkflow.SelectExport(report,InspectionKind.Short,f,cfg);Assert.Equal(483,selected.Result.Untestable);Assert.Equal(550,selected.ScopeTotal);Assert.Equal(1202,selected.BoardTotal);
        string dir=Path.Combine(Path.GetTempPath(),"ICT-scope-"+Guid.NewGuid());Directory.CreateDirectory(dir);string path=Path.Combine(dir,"盲点表.xlsx");
        try{new ExcelExporter(cfg).ExportFixed(selected.Result,path,new("","",Draft:true,BoardTotal:selected.BoardTotal,ScopeLabel:selected.ScopeLabel,ScopeTotal:selected.ScopeTotal,ScopeCoverage:selected.ScopeCoverage));Assert.Single(Directory.GetFiles(dir));using var wb=new XLWorkbook(path);var s=wb.Worksheets.First();Assert.Contains("550",s.Cell(6,1).GetString());Assert.Contains("12.18%",s.Cell(6,1).GetString());Assert.Contains("短路",s.Cell(7,1).GetString());ExcelExporter.Verify(path,selected.Result,true);}finally{File.Delete(path);Directory.Delete(dir);}
        Assert.Throws<InvalidOperationException>(()=>ReportWorkflow.SelectExport(report,InspectionKind.Short,f with{Status=TestState.Testable},cfg));
    }
    [Fact]public void AutoProjectDetectionDoesNotReusePreviousProjectAndUpgradeKeepsCustomRules()
    {
        var cfg=Config();var report=Sample();report.SourcePath="另一个报告.xls";foreach(var a in report.Analyses.Values){a.BoardNumber="OTHER";a.ProductNumber="OTHER";}Assert.Equal("",ProjectIdentification.Detect(report,cfg));
        report.SourcePath=cfg.SampleProfile+"-Coverage报告.xls";Assert.Equal("",ProjectIdentification.Detect(report,cfg));
        var unrelated=new Analysis{Components=[new(){RawReference="TVS1/TVS",RawValue="0.750V",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(unrelated,"");Assert.Equal("待确认",unrelated.Components[0].Type);Assert.True(unrelated.Pending>0);
        var older=RuleManagementService.Clone(cfg);older.Rules.RemoveAll(r=>r.RuleId.StartsWith("LEARN-"));older.ProjectPrefixTypes.Clear();var custom=new ClassificationRule{RuleId="USER",Stage="原因分类",Types=["电阻"],Reason="NO_POINT",Source="原用户规则",Priority=77};older.Rules.Add(custom);var upgraded=RuleManagementService.Upgrade(older,cfg);Assert.Contains(upgraded.Rules,r=>r.RuleId=="USER"&&r.Priority==77);Assert.Contains(upgraded.Rules,r=>r.RuleId.StartsWith("LEARN-"));
    }
}
