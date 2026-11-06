using System.Security.Cryptography;

namespace ICTCoverage.Core;

/// <summary>已保存报告的只读内存副本；识别、解析和会话标识使用同一份内容。</summary>
public sealed class CoverageSnapshot
{
    readonly byte[] content;
    public string SourcePath { get; }
    public string Fingerprint { get; }
    private CoverageSnapshot(string path,byte[] content)
    {
        SourcePath=Path.GetFullPath(path);this.content=content;Fingerprint=Convert.ToHexString(SHA256.HashData(content));
    }
    internal Stream OpenRead()=>new MemoryStream(content,writable:false);
    public static CoverageSnapshot Read(string path)
    {
        try
        {
            for(int attempt=0;attempt<2;attempt++)
            {
                var before=new FileInfo(path);long length=before.Length;var written=before.LastWriteTimeUtc;
                byte[] bytes;
                using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var buffer=new MemoryStream()) {source.CopyTo(buffer);bytes=buffer.ToArray();}
                var after=new FileInfo(path);
                if(after.Exists&&length==after.Length&&length==bytes.LongLength&&written==after.LastWriteTimeUtc)return new(path,bytes);
            }
            throw new IOException("Coverage 报告正在保存或发生变化，请等待 Excel/WPS 保存完成后重新读取。");
        }
        catch(IOException ex) when((ex.HResult&0xFFFF) is 32 or 33)
        {
            throw new IOException($"Coverage 报告被其他程序独占锁定，暂时无法读取。请保存并关闭 Excel/WPS 中的该报告后重试。\n文件：{path}",ex);
        }
        catch(UnauthorizedAccessException ex)
        {
            throw new IOException($"没有读取 Coverage 报告的权限，请检查文件权限或使用有权限的副本。\n文件：{path}",ex);
        }
    }
}
