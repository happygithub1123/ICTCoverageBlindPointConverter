using ICTCoverage.Core;
using System.Text.Json;
using Xunit;

namespace ICTCoverage.Tests;
public sealed class LocalProjectsFactAttribute:FactAttribute
{
    public LocalProjectsFactAttribute()
    {
        string path=Path.Combine(ConverterTests.Root,"outputs","external-fixtures.json");
        if(!File.Exists(path)){Skip="三组用户原始资料仅在本机保存，未随公开源码分发";return;}
        using var fixtures=JsonDocument.Parse(File.ReadAllText(path));
        if(fixtures.RootElement.EnumerateArray().Any(f=>!File.Exists(f.GetProperty("source").GetString())))Skip="三组历史Coverage原文件已不在配置路径；保留此前逐点验证记录，本次未重跑";
    }
}
public sealed class ExternalProjectTests
{
    [LocalProjectsFact]public void ThreeProvidedProjectPairsMatchEveryKnownReferenceAndReason()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(ConverterTests.Root,"outputs","external-fixtures.json")));var cfg=RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
        foreach(var data in fixture.RootElement.EnumerateArray())
        {
            var reader=new CoverageReader();var snap=CoverageSnapshot.Read(data.GetProperty("source").GetString()!);var report=reader.ReadReport(snap,reader.Locate(snap).Single());ReportWorkflow.Analyze(report,cfg);var missing=report.Analyses[InspectionKind.Missing];Assert.Equal(data.GetProperty("project").GetString(),report.ProjectProfile);Assert.Equal(data.GetProperty("expectedX").GetInt32(),missing.Untestable);
            Assert.True(missing.Pending==0,string.Join("\n",missing.Components.Where(c=>c.Pending).Select(c=>c.RawReference+": "+string.Join(";",c.Issues.Select(i=>i.Message)))));
            foreach(var expected in data.GetProperty("expected").EnumerateArray())
            {
                var item=missing.Components.Single(c=>c.Reference==expected.GetProperty("ref").GetString());Assert.Equal(expected.GetProperty("value").GetString(),item.Value);Assert.Equal(expected.GetProperty("reason").GetString(),cfg.ReasonText(item.ReasonCode).Replace("\n","").Replace("\r",""));
            }
            int expectedGroups=data.GetProperty("expected").EnumerateArray().Select(e=>(e.GetProperty("value").GetString(),e.GetProperty("reason").GetString())).Distinct().Count();
            Assert.Equal(expectedGroups,AnalysisEngine.Group(missing.Components.Where(c=>c.State==TestState.Untestable)).Count);
            if(report.ProjectProfile.StartsWith("65020997")){var c=missing.Components.Single(c=>c.Reference=="C101");Assert.Equal(TestState.Untestable,c.SourceState);Assert.Contains("短路群",cfg.ReasonText(c.ReasonCode));}
            string dir=Path.Combine(ConverterTests.Root,"outputs","v0.4.0-三组回归");Directory.CreateDirectory(dir);string path=Path.Combine(dir,report.ProjectProfile+"-缺件-盲点表.xlsx");
            var selection=ReportWorkflow.SelectExport(report,InspectionKind.Missing,new(),cfg);
            new ExcelExporter(cfg).ExportFixed(selection.Result,path,new(missing.BoardNumber,missing.ProductNumber,BoardTotal:selection.BoardTotal,ScopeTotal:selection.ScopeTotal,ScopeCoverage:selection.ScopeCoverage,ScopeLabel:selection.ScopeLabel));
            ExcelExporter.Verify(path,missing);
        }
    }
}
