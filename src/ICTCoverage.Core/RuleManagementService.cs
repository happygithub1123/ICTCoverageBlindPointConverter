using System.Text.Json;

namespace ICTCoverage.Core;

public record RuleChange(DateTimeOffset Time,string Action,string RuleId,string Reviewer,string Reason,string Before,string After);
public static class RuleManagementService
{
    public static bool CanEdit(ClassificationRule rule)=>rule.Stage is "原因分类" or "可测性扩展";
    public static string NewId(RuleConfig cfg)=>"CUSTOM-"+Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public static RuleConfig Update(RuleConfig original,ClassificationRule input,string reviewer,string justification,bool create=false,string? newReasonText=null)
    {
        if(!CanEdit(input))throw new InvalidDataException("核心管线规则只读，请新增或编辑分类规则");
        var next=Clone(original);var rule=JsonSerializer.Deserialize<ClassificationRule>(JsonSerializer.Serialize(input,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;
        if(string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(justification))throw new InvalidDataException("请填写修改人和修改理由");
        if(!string.IsNullOrWhiteSpace(newReasonText))next.Reasons[rule.Reason]=newReasonText.Trim();
        if(rule.Types.Length==1)foreach(var prefix in rule.Prefixes)
        {
            if(rule.Profile!=null){if(!next.ProjectPrefixTypes.TryGetValue(rule.Profile,out var projectMap))next.ProjectPrefixTypes[rule.Profile]=projectMap=[];projectMap[prefix]=rule.Types[0];continue;}
            if(next.PrefixTypes.TryGetValue(prefix,out var existing)&&existing!=rule.Types[0])throw new InvalidDataException($"前缀{prefix}已属于{existing}");
            next.PrefixTypes[prefix]=rule.Types[0];
        }
        if(rule.Suffix!=null&&!next.SuffixAliases.ContainsKey(rule.Suffix))
        {
            if(rule.Profile==null)next.SuffixAliases[rule.Suffix]=rule.Suffix;
            else{if(!next.ProjectSuffixAliases.TryGetValue(rule.Profile,out var aliases))next.ProjectSuffixAliases[rule.Profile]=aliases=[];aliases.TryAdd(rule.Suffix,rule.Suffix);}
        }
        string before="";
        if(create){if(next.Rules.Any(r=>r.RuleId==rule.RuleId))throw new InvalidDataException("规则编号重复");next.Rules.Add(rule);}
        else
        {
            var index=next.Rules.FindIndex(r=>r.RuleId==rule.RuleId);if(index<0||!CanEdit(next.Rules[index]))throw new InvalidDataException("规则不存在或不允许编辑");
            var old=next.Rules[index];before=JsonSerializer.Serialize(old,RuleConfig.JsonOptions);
            // 保留来源原文、旧ID及变更历史，表单只修改实际条件和输出。
            foreach(var pair in old.Metadata)if(!rule.Metadata.ContainsKey(pair.Key))rule.Metadata[pair.Key]=pair.Value;
            rule.LegacyIds=old.LegacyIds;next.Rules[index]=rule;
        }
        rule.Id=rule.RuleId;
        next.RuleChanges.Add(new(DateTimeOffset.Now,create?"新增":"修改",rule.RuleId,reviewer.Trim(),justification,before,JsonSerializer.Serialize(rule,RuleConfig.JsonOptions)));
        next.Validate();return next;
    }
    public static RuleConfig Archive(RuleConfig original,string id,bool archived,string reviewer,string justification)
    {
        var next=Clone(original);var rule=next.Rules.Single(r=>r.RuleId==id);
        if(!CanEdit(rule))throw new InvalidDataException("核心管线规则不允许删除");
        if(string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(justification))throw new InvalidDataException("请填写修改人和理由");
        var before=JsonSerializer.Serialize(rule,RuleConfig.JsonOptions);rule.Archived=archived;rule.Enabled=!archived;
        next.RuleChanges.Add(new(DateTimeOffset.Now,archived?"删除（可恢复）":"恢复",id,reviewer,justification,before,JsonSerializer.Serialize(rule,RuleConfig.JsonOptions)));next.Validate();return next;
    }
    public static RuleConfig Clone(RuleConfig cfg)=>JsonSerializer.Deserialize<RuleConfig>(JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;
    public static RuleConfig Upgrade(RuleConfig existing,RuleConfig defaults)
    {
        var next=Clone(existing);var fresh=Clone(defaults);
        foreach(var pair in fresh.Reasons)next.Reasons.TryAdd(pair.Key,pair.Value);
        foreach(var rule in fresh.Rules)if(rule.RuleId.StartsWith("LEARN-",StringComparison.Ordinal)&&!next.Rules.Any(r=>r.RuleId==rule.RuleId))next.Rules.Add(rule);
        foreach(var pair in fresh.ProjectPrefixTypes){if(!next.ProjectPrefixTypes.TryGetValue(pair.Key,out var map))next.ProjectPrefixTypes[pair.Key]=map=[];foreach(var item in pair.Value)map.TryAdd(item.Key,item.Value);}
        foreach(var pair in fresh.ProjectSuffixAliases){if(!next.ProjectSuffixAliases.TryGetValue(pair.Key,out var map))next.ProjectSuffixAliases[pair.Key]=map=[];foreach(var item in pair.Value)map.TryAdd(item.Key,item.Value);}
        next.Version=fresh.Version;next.Validate();return next;
    }
    public static void Save(RuleConfig cfg,string path)
    {
        cfg.Validate();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp=path+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
