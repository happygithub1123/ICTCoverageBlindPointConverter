using ICTCoverage.Core;
using ICTCoverage.WinForms;
using ClosedXML.Excel;
using Xunit;

namespace ICTCoverage.Tests;

public sealed class ReasonCatalogTests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    [Fact]public void DiodeTvsAndIcNeverMergeEvenWithIdenticalValueAndUnknownCause()
    {
        var cfg=Config();cfg.TypeValues.Clear();cfg.PrefixTypes["TVS"]="二极管";foreach(var rule in cfg.Rules.Where(r=>r.RuleId.StartsWith("BASE5-")))rule.Enabled=false;
        string[] diode=["D62","D64","D66","Z2","ZD1","ZD2","ZD3","ZD4","ZD9"];
        var tvs=Enumerable.Range(1,29).Where(i=>i!=21).Select(i=>"TVS"+i).ToArray();string[] ic=["M45","M46","M48","M52","M54","M55","M57","M58","M59","M61"];
        var a=new Analysis{Inspection=InspectionKind.Short,Components=diode.Concat(tvs).Concat(ic).Select((r,i)=>new Component{SourceRow=i+10,RawReference=r,RawValue="0.750V",StatusSymbol="Х"}).ToList()};new AnalysisEngine(cfg).Analyze(a,"");
        Assert.Equal(47,a.Untestable);Assert.Equal(47,a.Pending);Assert.Equal(0,a.DataIssues);var groups=AnalysisEngine.Group(a.Components);Assert.Equal(new[]{9,28,10},groups.Select(g=>g.Count));Assert.Equal(diode,groups[0].Items.Select(c=>c.Reference));Assert.Equal(tvs,groups[1].Items.Select(c=>c.Reference));Assert.Equal(ic,groups[2].Items.Select(c=>c.Reference));
        Assert.All(groups,g=>Assert.Equal("待确定",ClipboardService.GroupCells(g,cfg,48,true)[3]));
        string path=Path.Combine(ConverterTests.Root,"outputs","v0.4.1-分类分组验证.xlsx");new ExcelExporter(cfg).ExportFixed(a,path,new("分类验证",""));ExcelExporter.Verify(path,a);
        using var wb=new XLWorkbook(path);var(s,start,count)=ExcelExporter.LocateTemplate(wb);Assert.Equal(3,count);Assert.DoesNotContain("待审核",s.Cell(1,1).GetString());
        for(int i=0;i<3;i++){Assert.Equal("待确定",s.Cell(start+i,5).GetString());Assert.Equal(groups[i].Count,s.Cell(start+i,3).GetValue<int>());}
    }
    [Fact]public void DifferentTypesAndUnknownPrefixesAreSeparated()
    {
        var components=new[]{("R1","电阻"),("C1","电容"),("D1","二极管"),("Q1","三极管"),("ZZ1","待确认"),("YY1","待确认")}.Select(p=>new Component{Reference=p.Item1,Type=p.Item2,Value="same",ReasonCode="",State=TestState.Untestable}).ToList();Assert.Equal(6,AnalysisEngine.Group(components).Count);
    }
    [Fact]public void RenameDeleteRestoreKeepsRuleReferencesAndPersistsHistory()
    {
        var cfg=Config();cfg=ReasonManagementService.Upsert(cfg,null,"自定义原因","WHAPPY","新增");string code=cfg.Reasons.Single(p=>p.Value=="自定义原因").Key;
        var rule=new ClassificationRule{RuleId="CUSTOM-CATALOG",Source="测试",Stage="原因分类",Types=["电阻"],ExactValue="123",Reason=code};cfg=RuleManagementService.Update(cfg,rule,"WHAPPY","关联原因",true);
        cfg=ReasonManagementService.Upsert(cfg,code,"修改后的原因","WHAPPY","改名");Assert.Equal("修改后的原因",cfg.ReasonText(code));Assert.Equal(code,cfg.Rules.Single(r=>r.RuleId==rule.RuleId).Reason);
        int count=cfg.ReasonChanges.Count;cfg=ReasonManagementService.Upsert(cfg,code,"修改后的原因","WHAPPY","原样保存");Assert.Equal(count,cfg.ReasonChanges.Count);
        cfg=ReasonManagementService.Archive(cfg,code,true,"WHAPPY","删除");Assert.False(cfg.IsReasonActive(code));Assert.DoesNotContain(cfg.ActiveReasons,p=>p.Key==code);Assert.Equal("待确定",cfg.ReasonText(code));
        var a=new Analysis{Components=[new(){RawReference="R123",RawValue="123",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(a,"");Assert.Equal("",a.Components[0].ReasonCode);Assert.Equal(0,a.DataIssues);
        cfg=ReasonManagementService.Archive(cfg,code,false,"WHAPPY","恢复");new AnalysisEngine(cfg).Analyze(a,"");Assert.Equal(code,a.Components[0].ReasonCode);Assert.Equal(4,cfg.ReasonChanges.Count);
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{RuleManagementService.Save(cfg,path);var saved=RuleConfig.Load(path);Assert.Equal(4,saved.ReasonChanges.Count);Assert.True(saved.IsReasonActive(code));Assert.Equal("自定义原因",saved.ReasonChanges[0].After);}finally{File.Delete(path);}
        Assert.Throws<InvalidDataException>(()=>ReasonManagementService.Upsert(cfg,null,"修改后的原因","WHAPPY","重复名称"));
    }
    [Fact]public void ArchivedManualCauseCanBeRestoredWithoutLosingFinalRecord()
    {
        var cfg=Config();Analysis Fresh(){var a=new Analysis{SourceFingerprint="fixture",Components=[new(){SourceRow=10,RawReference="R1",RawValue="1K",StatusSymbol="X"}]};new AnalysisEngine(cfg).Analyze(a,"");return a;}
        var old=Fresh();AnalysisEngine.Edit(old,old.Components[0],"R1","电阻","1K",TestState.Untestable,"NO_POINT","","人工原因",cfg);var saved=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,old}}};
        cfg=ReasonManagementService.Archive(cfg,"NO_POINT",true,"WHAPPY","停用");var current=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,Fresh()}}};ReportWorkflow.Restore(current,saved,cfg);Assert.Single(ManualResults.Get(current,cfg));Assert.Equal("待确定",ManualResults.Get(current,cfg)[0].Reason);
        cfg=ReasonManagementService.Archive(cfg,"NO_POINT",false,"WHAPPY","恢复");var reopened=new CoverageReport{SourceFingerprint="fixture",Analyses={{InspectionKind.Missing,Fresh()}}};ReportWorkflow.Restore(reopened,current,cfg);Assert.Equal("NO_POINT",reopened.Analyses[InspectionKind.Missing].Components[0].ReasonCode);
    }
    [Fact]public void ConflictingCausesExportAsUndeterminedWithoutGuessing()
    {
        var cfg=Config();var a=new Analysis{Components=[new(){Reference="C1",Type="电容",Value="1uF",State=TestState.Untestable,SourceState=TestState.Untestable,ReasonCode="NO_POINT",Issues=[new("rule_conflict","原因冲突")]}]};
        var group=Assert.Single(AnalysisEngine.Group(a.Components));Assert.Equal("",group.ReasonCode);Assert.Equal("待确定",ClipboardService.GroupCells(group,cfg,48,true)[3]);Assert.Single(AnalysisEngine.Filter(a,new(Reason:"待确定"),cfg));
    }
}
