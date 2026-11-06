using Xunit;
using ICTCoverage.Core;
using ICTCoverage.WinForms;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using System.Text;

namespace ICTCoverage.Tests;

public sealed class ConverterTests
{
    public static string Root
    {
        get {var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"ICTCoverageBlindPointConverter.sln")))d=d.Parent;return d?.FullName??throw new Exception("找不到工程根目录");}
    }
    static string Sample=>Directory.GetFiles(Path.Combine(Root,"samples"),"*.xls").Single();
    static string Template=>Directory.GetFiles(Path.Combine(Root,"samples"),"*.xlsx").Single();
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(Root,"config","rules.json"));
    static Analysis ReadSample()
    {
        var reader=new CoverageReader();var maps=reader.Locate(Sample);Assert.Single(maps);
        var a=reader.Read(Sample,maps[0]);new AnalysisEngine(Config()).Analyze(a,"MG10P0B3270350-P25086380101");return a;
    }
    static string Output(string name){var d=Path.Combine(Root,"outputs");Directory.CreateDirectory(d);return Path.Combine(d,name);}
    [Fact] public void RealSample_ReconcilesEveryComponentAndReason()
    {
        var a=ReadSample();Assert.Equal(1202,a.Total);Assert.Equal(1000,a.Untestable);Assert.Equal(202,a.Testable);Assert.Equal(1000,a.SourceUntestable);
        Assert.Equal(0,a.Pending);Assert.False(a.Components.Single(c=>c.Reference=="Q7").Pending);Assert.Equal(TestState.Testable,a.Components.Single(c=>c.Reference=="Q7").SourceState);
        var all=a.Components.Where(c=>c.State==TestState.Untestable).ToList();Assert.Equal(1000,all.Select(c=>c.Reference).Distinct().Count());Assert.Equal(99,AnalysisEngine.Group(all).Count);
        var expected=new Dictionary<string,int>{{"NO_POINT",744},{"PARALLEL",156},{"DQ",53},{"CONNECTOR",21},{"IC",13},{"ACTIVE",5},{"SMALL_PF",4},{"BELOW_STANDARD",4}};
        foreach(var pair in expected)Assert.Equal(pair.Value,all.Count(c=>c.ReasonCode==pair.Key));
        Assert.Equal(2,a.Components.Count(c=>c.Yellow&&c.State==TestState.Testable));
        Assert.True(a.Components.Single(c=>c.Reference=="Q7").Yellow);
    }
    [Fact] public void SampleExport_PreservesTemplateAndCorrectsManualErrors()
    {
        var a=ReadSample();var exporter=new ExcelExporter(Config());var path=Output("样例-自动生成盲点表-待审核.xlsx");
        exporter.Export(a,Template,path,new(a.BoardNumber,a.ProductNumber,"",Draft:true));exporter.ExportAudit(a,Output("样例-核对报告.xlsx"),Template);
        ExcelExporter.Verify(path,a);using var source=new XLWorkbook(Template);using var result=new XLWorkbook(path);
        var(s,start,count)=ExcelExporter.LocateTemplate(result);var original=source.Worksheet(s.Name);
        Assert.Equal(99,count);Assert.Equal(original.MergedRanges.Select(r=>r.RangeAddress.ToString()),s.MergedRanges.Select(r=>r.RangeAddress.ToString()));
        for(int col=1;col<=6;col++)Assert.Equal(original.Column(col).Width,s.Column(col).Width,5);
        for(int r=1;r<=8;r++)for(int c=1;c<=6;c++)Assert.Equal(original.Cell(r,c).Style,s.Cell(r,c).Style);
        for(int r=108;r<=121;r++)for(int c=1;c<=6;c++){Assert.Equal(original.Cell(r,c).Value,s.Cell(r,c).Value);Assert.Equal(original.Cell(r,c).Style,s.Cell(r,c).Style);}
        Assert.Equal(original.PageSetup.PaperSize,s.PageSetup.PaperSize);Assert.Equal(original.PageSetup.Scale,s.PageSetup.Scale);
        var refs=Enumerable.Range(start,count).SelectMany(r=>ExcelExporter.SplitRefs(s.Cell(r,2).GetString())).ToArray();Assert.Contains("C246",refs);Assert.Contains("RA4",refs);Assert.Contains("P21001",refs);
        var diffs=exporter.Compare(a,Template);Assert.Contains(diffs,d=>d.Reference=="C246"&&d.Kind=="样表遗漏");Assert.Contains(diffs,d=>d.Reference=="RA4"&&d.Kind=="样表遗漏");Assert.Contains(diffs,d=>d.Reference=="P21001"&&d.Kind=="样表遗漏");
    }
    [Theory][InlineData("电阻")][InlineData("电容")][InlineData("电感")][InlineData("二极管")]
    public void FiltersAndClipboard_PreserveCountAndGrouping(string type)
    {
        var a=ReadSample();var cfg=Config();var items=AnalysisEngine.Filter(a,new(Type:type),cfg);Assert.NotEmpty(items);Assert.All(items,c=>Assert.Equal(type,c.Type));
        foreach(var g in AnalysisEngine.Group(items))
        {
            if(g.Pending){Assert.Throws<InvalidOperationException>(()=>ClipboardService.GroupCells(g,cfg,48,true));continue;}
            var cells=ClipboardService.GroupCells(g,cfg,48,true);Assert.Equal(g.Count,ExcelExporter.SplitRefs(cells[0]).Length);Assert.EndsWith(".",cells[0]);Assert.Equal(g.Count.ToString(),cells[1]);Assert.Equal(g.Value,cells[2]);
            string html=ClipboardService.BuildHtml(cells);var bytes=Encoding.UTF8.GetBytes(html);int start=int.Parse(html.Split("StartFragment:")[1][..10]),end=int.Parse(html.Split("EndFragment:")[1][..10]);string fragment=Encoding.UTF8.GetString(bytes[start..end]);Assert.StartsWith("<table><tr>",fragment);Assert.Equal(4,fragment.Split("<td ").Length-1);Assert.Single(fragment.Split("<tr>").Skip(1));
        }
        Assert.Empty(AnalysisEngine.Filter(a,new(Type:type,Reference:"DOES_NOT_EXIST"),cfg));
    }
    [Fact] public void ResistorExample_FirstGroupAndPrefixFamilies()
    {
        var a=ReadSample();var items=AnalysisEngine.Filter(a,new(Type:"电阻",Value:"0.Ω",Reason:"无测试点"),Config());Assert.Equal(165,items.Count);
        Assert.Contains(items,c=>c.Reference=="RA89");Assert.Contains(items,c=>c.Reference=="RB15");Assert.Contains(items,c=>c.Reference=="BD9");
        var refs=ReferenceFormatter.Format(items.Select(c=>c.Reference));Assert.StartsWith("R103.R104.R105.R12016.R12026.R12028.R122.",refs);
    }
    [Theory][InlineData("R103/NP","",TestState.Unknown)][InlineData("R103/NP","√",TestState.Testable)][InlineData("R103","Х",TestState.Untestable)][InlineData("R103","X",TestState.Untestable)][InlineData("R103","×",TestState.Untestable)][InlineData("R103","",TestState.Unknown)][InlineData("R103","√",TestState.Testable)]
    public void StateRules_DontTreatYellowOrBlanksAsUntestable(string reference,string symbol,TestState expected)
    {
        var a=new Analysis{Components=[new(){RawReference=reference,StatusSymbol=symbol,RawValue="1",Yellow=true}]};new AnalysisEngine(Config()).Analyze(a,"");Assert.Equal(expected,a.Components[0].State);
    }
    [Fact] public void UnknownSuffixDuplicateAndManualEdits_AreAuditedAndBlocked()
    {
        var a=new Analysis{Components=[new(){SourceRow=1,RawReference="R1/UNKNOWN",StatusSymbol="X",RawValue="1"},new(){SourceRow=2,RawReference="R1/NP",StatusSymbol="X",RawValue="1"},new(){SourceRow=3,RawReference="ZZ1/NP",StatusSymbol="X",RawValue=""}]};var cfg=Config();new AnalysisEngine(cfg).Analyze(a,"");Assert.Equal(3,a.Pending);
        var group=new BlindGroup("1","NO_POINT",a.Components.Take(2).ToList());Assert.Throws<InvalidOperationException>(()=>ClipboardService.GroupCells(group,cfg,48,false));
        Assert.Throws<InvalidOperationException>(()=>new ExcelExporter(cfg).Export(a,Template,Output("blocked.xlsx"),new("","")));
        AnalysisEngine.Edit(a,a.Components[0],"R2","电阻","1",TestState.Untestable,"NO_POINT","","核对原记录修正重复位号",cfg);Assert.Single(a.Edits);Assert.False(a.Components[1].Pending);Assert.True(a.Components[2].Pending);
    }
    [Fact] public void ProjectSpecials_DoNotLeakToOtherBoards()
    {
        var a=new Analysis{Components=[new(){RawReference="C1/NP",RawValue="10.00pF",StatusSymbol="X"},new(){RawReference="R1/NP",RawValue="1",StatusSymbol="X"}]};new AnalysisEngine(Config()).Analyze(a,"");Assert.Equal("NO_POINT",a.Components[0].ReasonCode);Assert.Equal("1",a.Components[1].Value);
    }
    [Fact] public void Reader_HandlesMovedColumnsAndMultipleCandidates()
    {
        string path=Output("test-shifted.xls");using(var wb=new HSSFWorkbook())
        {
            foreach(var name in new[]{"检测A","检测B"}){var s=wb.CreateSheet(name);s.CreateRow(2).CreateCell(9).SetCellValue("ICT");s.AddMergedRegion(new CellRangeAddress(2,2,9,11));var header=s.CreateRow(3);header.CreateCell(10).SetCellValue("缺件");header.CreateCell(6).SetCellValue("标准值");var row=s.CreateRow(4);row.CreateCell(4).SetCellValue("R1/NP");row.CreateCell(6).SetCellValue("22.00K");row.CreateCell(10).SetCellValue("X");}
            using var stream=File.Create(path);wb.Write(stream);
        }
        var reader=new CoverageReader();var maps=reader.Locate(path);Assert.Equal(2,maps.Count);Assert.All(maps,m=>{Assert.Equal(4,m.ReferenceColumn);Assert.Equal(6,m.ValueColumn);Assert.Equal(10,m.StatusColumn);});var a=reader.Read(path,maps[0]);Assert.Single(a.Components);Assert.Equal("22.00K",a.Components[0].RawValue);
    }
    [Theory][InlineData(0)][InlineData(2)][InlineData(105)]
    public void VariableGroupCounts_MoveFooterWithoutDamage(int count)
    {
        var a=new Analysis{Components=Enumerable.Range(1,count).Select(i=>new Component{SourceRow=i,RawReference=$"R{i}/NP",RawValue=$"{i}K",StatusSymbol="X"}).ToList()};new AnalysisEngine(Config()).Analyze(a,"");string path=Output($"test-groups-{count}.xlsx");new ExcelExporter(Config()).Export(a,Template,path,new("新光板","新成品"));
        using var wb=new XLWorkbook(path);var(s,_,actual)=ExcelExporter.LocateTemplate(wb);Assert.Equal(count,actual);Assert.Contains("编写人",s.Cell(119+count-99,1).GetString());Assert.Contains("日期",s.Cell(121+count-99,1).GetString());
    }
    [Fact] public void DamagedOrLockedFiles_FailExplicitly()
    {
        string bad=Output("test-broken.xls");File.WriteAllText(bad,"not an Excel file");Assert.ThrowsAny<Exception>(()=>new CoverageReader().Locate(bad));
        string path=Output("test-locked.xlsx");File.WriteAllText(path,"locked");using var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Assert.ThrowsAny<IOException>(()=>new ExcelExporter(Config()).Export(ReadSample(),Template,path,new("","",Draft:true)));
    }
}
