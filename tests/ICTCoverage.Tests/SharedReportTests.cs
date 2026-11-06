using ICTCoverage.Core;
using System.Security.Cryptography;
using Xunit;

namespace ICTCoverage.Tests;

public sealed class SharedReportTests
{
    static string Source=>Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();
    [Theory][InlineData(FileShare.Read)][InlineData(FileShare.ReadWrite)]
    public void ReportCanBeReadWhileAnEditorHasItOpenForWriting(FileShare sharing)
    {
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xls");File.Copy(Source,file);
        try
        {
            using var editor=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,sharing);
            var reader=new CoverageReader();var snapshot=CoverageSnapshot.Read(file);var fields=reader.Locate(snapshot);Assert.Single(fields);var a=reader.Read(snapshot,fields[0]);new AnalysisEngine(RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"))).Analyze(a,"MG10P0B3270350-P25086380101");
            Assert.Equal(1202,a.Total);Assert.Equal(1000,a.Untestable);Assert.Equal(202,a.Testable);Assert.Equal(0,a.Pending);Assert.Equal(snapshot.Fingerprint,a.SourceFingerprint);
            // 兼容原有路径接口，不能只修复新接口。
            Assert.Single(reader.Locate(file));Assert.Equal(1202,reader.Read(file,fields[0]).Total);
        }finally{File.Delete(file);}
    }
    [Fact]public void ExclusiveLockShowsAnActionableChineseMessage()
    {
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xls");File.Copy(Source,file);
        try{using var editor=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);var error=Assert.Throws<IOException>(()=>CoverageSnapshot.Read(file));Assert.Contains("独占锁定",error.Message);Assert.Contains("Excel/WPS",error.Message);Assert.Contains("保存并关闭",error.Message);Assert.Contains(file,error.Message);}
        finally{File.Delete(file);}
    }
    [Fact]public void FieldDiscoveryAndParsingUseTheSameSavedSnapshot()
    {
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xls");File.Copy(Source,file);
        try
        {
            var originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));var snapshot=CoverageSnapshot.Read(file);var reader=new CoverageReader();var fields=reader.Locate(snapshot);
            File.WriteAllText(file,"模拟编辑器随后保存了另一版内容");using var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var a=reader.Read(snapshot,fields.Single());Assert.Equal(1202,a.Total);Assert.Equal(originalHash,a.SourceFingerprint);Assert.Equal(Path.GetFullPath(file),a.SourcePath);
        }finally{File.Delete(file);}
    }
}
