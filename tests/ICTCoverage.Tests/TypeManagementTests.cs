using System.Diagnostics;
using ICTCoverage.Core;
using Xunit;
namespace ICTCoverage.Tests;
public sealed class TypeManagementTests
{
    static RuleConfig Config()=>RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
    static Analysis Fresh(RuleConfig cfg,int count=1)
    {
        var a=new Analysis{Components=Enumerable.Range(1,count).Select(i=>new Component{Sheet="S",SourceRow=i,RawReference="R"+i+"/NP",RawValue="1K",StatusSymbol="X"}).ToList()};new AnalysisEngine(cfg).Analyze(a,"");return a;
    }
    [Fact]public void AddEditArchiveRestoreTypePreservesSourcesAndPrefixOwnership()
    {
        var original=Config();var added=TypeManagementService.Upsert(original,null,"保险丝",["ZZF","ZZP"],"WHAPPY","新增分类");
        Assert.Contains("保险丝",added.KnownTypes);Assert.DoesNotContain("保险丝",original.KnownTypes);Assert.Equal("保险丝",added.PrefixTypes["ZZF"]);
        Assert.Throws<InvalidDataException>(()=>TypeManagementService.Upsert(added,null,"冲突分类",["R"],"WHAPPY","冲突检查"));
        var renamed=TypeManagementService.Upsert(added,"保险丝","熔断器",["ZZF"],"WHAPPY","改名和前缀");Assert.DoesNotContain("ZZP",renamed.PrefixTypes.Keys);Assert.Equal("熔断器",TypeManagementService.ResolveName(renamed,"保险丝"));
        var archived=TypeManagementService.Archive(renamed,"熔断器",true,"WHAPPY","删除可恢复");Assert.DoesNotContain("熔断器",archived.KnownTypes);Assert.Equal("熔断器",archived.PrefixTypes["ZZF"]);
        var recovered=TypeManagementService.Archive(archived,"熔断器",false,"WHAPPY","恢复");Assert.Contains("熔断器",recovered.KnownTypes);Assert.Equal(4,recovered.TypeChanges.Count);
        Assert.Equal(original.Rules.Select(r=>r.Source),recovered.Rules.Select(r=>r.Source));Assert.Equal(original.Rules.Count,recovered.Rules.Count);
    }
    [Fact]public void RenameBuiltInTypeUpdatesRulesAndRestoresOldManualNames()
    {
        var cfg=Config();var a=Fresh(cfg);var time=DateTimeOffset.Now;var saved=new CoverageReport{Analyses={{InspectionKind.Missing,new Analysis{Corrections={{1,new(time,1,"R1","电阻","1K","NO_POINT","")}}}}}};
        var next=TypeManagementService.Upsert(cfg,"电阻","电阻器",cfg.PrefixTypes.Where(p=>p.Value=="电阻").Select(p=>p.Key),"WHAPPY","分类改名");
        Assert.DoesNotContain("电阻",next.KnownTypes);Assert.Contains("电阻器",next.KnownTypes);Assert.All(next.Rules,r=>Assert.DoesNotContain("电阻",r.Types));Assert.Equal(cfg.Rules.Select(r=>r.Source),next.Rules.Select(r=>r.Source));
        var fresh=new CoverageReport{Analyses={{InspectionKind.Missing,Fresh(next)}}};ReportWorkflow.Restore(fresh,saved,next);Assert.Equal("电阻器",fresh.Analyses[InspectionKind.Missing].Components[0].Type);Assert.Equal(time,fresh.Analyses[InspectionKind.Missing].Corrections[1].Time);Assert.Empty(fresh.Analyses[InspectionKind.Missing].Edits);
    }
    [Fact]public void DeletedTypeDoesNotLoseManualResultsOrChangeSymbols()
    {
        var cfg=Config();var time=DateTimeOffset.Now;var saved=new CoverageReport{Analyses={{InspectionKind.Missing,new Analysis{Corrections={{1,new(time,1,"R1","电阻","1K","NO_POINT","")}}}}}};
        var deleted=TypeManagementService.Archive(cfg,"电阻",true,"WHAPPY","停用");var report=new CoverageReport{Analyses={{InspectionKind.Missing,Fresh(deleted)}}};ReportWorkflow.Restore(report,saved,deleted);
        var c=report.Analyses[InspectionKind.Missing].Components[0];Assert.Equal("待确认",c.Type);Assert.Equal(TestState.Untestable,c.SourceState);Assert.Single(ManualResults.Get(report,deleted));
        var restored=TypeManagementService.Archive(deleted,"电阻",false,"WHAPPY","恢复");var reopened=new CoverageReport{Analyses={{InspectionKind.Missing,Fresh(restored)}}};ReportWorkflow.Restore(reopened,report,restored);Assert.Equal("电阻",reopened.Analyses[InspectionKind.Missing].Components[0].Type);Assert.Equal("NO_POINT",reopened.Analyses[InspectionKind.Missing].Components[0].ReasonCode);
    }
    [Fact]public void RestoreThousandsOfFinalCorrectionsOnceWithoutRecreatingEditJson()
    {
        var cfg=Config();var current=Fresh(cfg,1740);var saved=new Analysis();var time=DateTimeOffset.Now;
        foreach(var c in current.Components)saved.Corrections[c.SourceRow]=new(time,c.SourceRow,c.Reference,c.Type,c.Value,"NO_POINT","");
        var report=new CoverageReport{Analyses={{InspectionKind.Missing,current}}};var watch=Stopwatch.StartNew();ReportWorkflow.Restore(report,new(){Analyses={{InspectionKind.Missing,saved}}},cfg);
        Assert.Equal(1740,current.Corrections.Count);Assert.Empty(current.Edits);Assert.Equal(1740,ManualResults.Get(report,cfg).Count);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(3),"恢复最终值应只扫描一次整板");Assert.All(current.Components,c=>Assert.False(c.HasDataIssues));
    }
}
