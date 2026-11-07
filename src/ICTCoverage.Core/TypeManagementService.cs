using System.Text.Json;
using System.Text.RegularExpressions;
namespace ICTCoverage.Core;

public record TypeChange(DateTimeOffset Time,string Action,string Before,string After,string Prefixes,string Reviewer,string Justification);
public static class TypeManagementService
{
    static void Check(string reviewer,string why){if(string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(why))throw new InvalidDataException("请填写修改人和说明");}
    public static string ResolveName(RuleConfig cfg,string name)
    {
        if(cfg.TypeAliases.Count==0)return name;var seen=new HashSet<string>();while(cfg.TypeAliases.TryGetValue(name,out var next)&&seen.Add(name))name=next;return name;
    }
    public static RuleConfig Upsert(RuleConfig original,string? previous,string name,IEnumerable<string> prefixes,string reviewer,string why)
    {
        Check(reviewer,why);name=name.Trim();if(name.Length is 0 or >30||name is "全部" or "待确认")throw new InvalidDataException("请输入1—30个字符的分类名称，不能使用全部或待确认");
        if(previous!=null&&!original.AllTypes.Contains(previous))throw new InvalidDataException("分类不存在");
        if(previous!=name&&original.AllTypes.Contains(name))throw new InvalidDataException("已有同名分类，请编辑或恢复已有分类");
        var tokens=prefixes.Select(p=>p.Trim().ToUpperInvariant()).Where(p=>p.Length>0).Distinct().ToArray();
        if(tokens.Any(p=>!Regex.IsMatch(p,"^[A-Z]+$")))throw new InvalidDataException("前缀只填写英文字母，例如 R、RA、TVS");
        foreach(string prefix in tokens)if(original.PrefixTypes.TryGetValue(prefix,out var assigned)&&assigned!=previous&&assigned!=name)throw new InvalidDataException($"前缀 {prefix} 已属于 {assigned}，请先编辑该分类");
        var next=RuleManagementService.Clone(original);
        if(previous!=null&&previous!=name)
        {
            foreach(var map in next.ProjectPrefixTypes.Values.Prepend(next.PrefixTypes))foreach(string key in map.Keys.ToArray())if(map[key]==previous)map[key]=name;
            if(next.TypeValues.Remove(previous,out var value))next.TypeValues[name]=value;
            for(int i=0;i<next.ComponentOverrides.Count;i++)if(next.ComponentOverrides[i].Type==previous)next.ComponentOverrides[i]=next.ComponentOverrides[i] with{Type=name};
            foreach(var rule in next.Rules.Where(r=>r.Types.Contains(previous)))
            {
                string before=JsonSerializer.Serialize(rule,RuleConfig.JsonOptions);rule.Types=rule.Types.Select(t=>t==previous?name:t).Distinct().ToArray();
                next.RuleChanges.Add(new(DateTimeOffset.Now,"分类改名同步",rule.RuleId,reviewer,why,before,JsonSerializer.Serialize(rule,RuleConfig.JsonOptions)));
            }
            next.CustomTypes.Remove(previous);next.ArchivedTypes.Remove(previous);next.TypeAliases.Remove(name);
            foreach(string key in next.TypeAliases.Keys.ToArray())if(next.TypeAliases[key]==previous)next.TypeAliases[key]=name;
            next.TypeAliases[previous]=name;
        }
        foreach(string prefix in next.PrefixTypes.Where(p=>p.Value==name).Select(p=>p.Key).ToArray())next.PrefixTypes.Remove(prefix);
        foreach(string prefix in tokens)next.PrefixTypes[prefix]=name;
        next.CustomTypes.Add(name);next.ArchivedTypes.Remove(name);
        string oldPrefixes=string.Join(",",original.PrefixTypes.Where(p=>p.Value==previous).Select(p=>p.Key).Order());
        if(previous==name&&!original.ArchivedTypes.Contains(name)&&oldPrefixes==string.Join(",",tokens.Order()))return next;
        next.TypeChanges.Add(new(DateTimeOffset.Now,previous==null?"新增":"修改",previous??"",name,string.Join("、",tokens),reviewer.Trim(),why.Trim()));next.Validate();return next;
    }
    public static RuleConfig Archive(RuleConfig original,string name,bool archived,string reviewer,string why)
    {
        Check(reviewer,why);if(name is "全部" or "待确认"||!original.AllTypes.Contains(name))throw new InvalidDataException("分类不存在或为固定选项");
        var next=RuleManagementService.Clone(original);if(next.ArchivedTypes.Contains(name)==archived)return next;
        if(archived)next.ArchivedTypes.Add(name);else next.ArchivedTypes.Remove(name);
        next.TypeChanges.Add(new(DateTimeOffset.Now,archived?"删除（可恢复）":"恢复",name,name,"",reviewer.Trim(),why.Trim()));next.Validate();return next;
    }
}
