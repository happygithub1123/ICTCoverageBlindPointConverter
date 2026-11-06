namespace ICTCoverage.Core;

public record ReasonChange(DateTimeOffset Time,string Action,string Code,string Before,string After,string Reviewer,string Justification);
public static class ReasonManagementService
{
    public static RuleConfig Upsert(RuleConfig original,string? code,string text,string reviewer,string justification)
    {
        text=text.Trim();CheckReviewer(reviewer,justification);
        if(text.Length==0||text.Length>1000)throw new InvalidDataException("盲点原因请输入1—1000个字符");
        var next=RuleManagementService.Clone(original);code=string.IsNullOrWhiteSpace(code)?"CUSTOM_REASON_"+Guid.NewGuid().ToString("N")[..8]:code;
        if(next.ActiveReasons.Any(p=>p.Key!=code&&p.Value.Trim()==text))throw new InvalidDataException("已有同名盲点原因，请直接使用或修改已有原因");
        bool exists=next.Reasons.TryGetValue(code,out var before);if(exists&&before==text)return next;
        next.Reasons[code]=text;next.ReasonChanges.Add(new(DateTimeOffset.Now,exists?"修改":"新增",code,before??"",text,reviewer.Trim(),justification.Trim()));next.Validate();return next;
    }
    public static RuleConfig Archive(RuleConfig original,string code,bool archived,string reviewer,string justification)
    {
        CheckReviewer(reviewer,justification);var next=RuleManagementService.Clone(original);
        if(!next.Reasons.TryGetValue(code,out var text))throw new InvalidDataException("盲点原因不存在");
        if(next.ArchivedReasons.Contains(code)==archived)return next;
        if(archived)next.ArchivedReasons.Add(code);else
        {
            if(next.ActiveReasons.Any(p=>p.Key!=code&&p.Value.Trim()==text.Trim()))throw new InvalidDataException("已有同名原因，请先修改名称再恢复");
            next.ArchivedReasons.Remove(code);
        }
        next.ReasonChanges.Add(new(DateTimeOffset.Now,archived?"删除":"恢复",code,text,text,reviewer.Trim(),justification.Trim()));next.Validate();return next;
    }
    static void CheckReviewer(string reviewer,string justification){if(string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(justification))throw new InvalidDataException("请填写修改人和修改说明");}
}
