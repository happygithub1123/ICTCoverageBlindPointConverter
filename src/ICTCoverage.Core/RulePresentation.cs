namespace ICTCoverage.Core;

public static class RulePresentation
{
    public static string CurrentExecution(ClassificationRule rule)
    {
        if(rule.Archived)return "已删除，可恢复";
        if(!rule.Enabled)return "已停用";
        if(rule.ReviewOnly)return "仅作原因参考，不改变检测结果";
        if(rule.RuleId=="G-01")return "按元件类别、标准值和原因分组；TVS单独成组";
        if(rule.Stage=="导出")return "未知原因写待确定；不同类别分行，数据问题单独提示";
        return rule.Stage is "原因分类" or "可测性扩展"?"仅对 Coverage 打 X 的元件归类原因":"固定读取、整理或校验步骤";
    }
    public static string HistoricalBasis(ClassificationRule rule)
    {
        if(rule.Metadata.TryGetValue("historicalEvidence",out var history)&&history.ValueKind==System.Text.Json.JsonValueKind.Object&&history.TryGetProperty("originalStatus",out var status)&&status.ValueKind==System.Text.Json.JsonValueKind.String)return "历史："+status.GetString();
        return string.IsNullOrWhiteSpace(rule.Status)?"未记录":"历史："+rule.Status;
    }
}
