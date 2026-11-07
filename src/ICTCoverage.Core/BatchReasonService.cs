using System.Text.Json;
namespace ICTCoverage.Core;

public record CauseTarget(InspectionKind Inspection,string Sheet,int SourceRow,string Fingerprint="");
public static class BatchReasonService
{
    public static CoverageReport Prepare(CoverageReport report,IEnumerable<CauseTarget> selection,string reason,RuleConfig cfg)
    {
        if(!cfg.IsReasonActive(reason))throw new InvalidDataException("请选择有效的盲点原因");
        var targets=selection.Distinct().ToArray();if(targets.Length==0)throw new InvalidDataException("请先勾选需要确认的记录");
        var next=report.Copy();
        var resolved=new List<(Analysis Analysis,Component Item)>();
        var bySource=next.Analyses.ToDictionary(p=>p.Key,p=>p.Value.Components.ToLookup(c=>(c.Sheet,c.SourceRow)));
        foreach(var target in targets)
        {
            if(target.Fingerprint.Length>0&&target.Fingerprint!=report.SourceFingerprint)throw new InvalidDataException("选择记录不属于当前报告");
            if(!next.Analyses.TryGetValue(target.Inspection,out var a))throw new InvalidDataException("选中记录的检测项目不存在");
            var c=bySource[target.Inspection][(target.Sheet,target.SourceRow)].SingleOrDefault()??throw new InvalidDataException("选中源记录不存在");
            if(c.SourceState!=TestState.Untestable||c.HasDataIssues)throw new InvalidDataException(c.Reference+"存在文件数据问题或不是Х记录，请单独处理");
            resolved.Add((a,c));
        }
        foreach(var (a,c) in resolved)
        {
            if(c.ReasonCode==reason&&!c.Issues.Any(i=>i.Code is "classification" or "rule_conflict"))continue;
            string before=JsonSerializer.Serialize(c,RuleConfig.JsonOptions);c.ReasonCode=reason;c.ManuallyReviewed=true;c.Issues.RemoveAll(i=>i.Code is "classification" or "rule_conflict");var time=DateTimeOffset.Now;
            a.Edits.Add(new(time,c.SourceRow,before,JsonSerializer.Serialize(c,RuleConfig.JsonOptions),"批量确认盲点原因",Environment.UserName,cfg.Version,a.Inspection));a.Corrections[c.SourceRow]=new(time,c.SourceRow,c.Reference,c.Type,c.Value,c.ReasonCode,c.Note);
        }
        return next;
    }
    public static void Save(CoverageReport next,string path)
    {
        var snapshot=new CoverageReport{SourcePath=next.SourcePath,SourceFingerprint=next.SourceFingerprint,ProjectProfile=next.ProjectProfile,Analyses=next.Analyses.ToDictionary(p=>p.Key,p=>new Analysis{Inspection=p.Key,SourceFingerprint=next.SourceFingerprint,Corrections=ManualResults.Latest(p.Value).ToDictionary(c=>c.SourceRow)})};
        string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(temp,JsonSerializer.Serialize(snapshot,RuleConfig.JsonOptions));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
