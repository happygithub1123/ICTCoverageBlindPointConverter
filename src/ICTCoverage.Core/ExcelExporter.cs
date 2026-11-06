using ClosedXML.Excel;
using System.Text.RegularExpressions;

namespace ICTCoverage.Core;

public record ExportOptions(string BoardNumber,string ProductNumber,string NeedleRate="",string DefaultNote="",int WrapWidth=48,bool Draft=false);
public record AuditDifference(string Kind,string Reference,string Detail);
public sealed class ExcelExporter(RuleConfig cfg)
{
    private static string NormalizeReason(string s)=>s.Replace("\\n","").Replace("\r","").Replace("\n","").Replace(" ","");
    public static (IXLWorksheet Sheet,int Start,int Count) LocateTemplate(XLWorkbook wb)
    {
        foreach(var s in wb.Worksheets)
        foreach(var row in s.RowsUsed().Take(100))
            if(row.Cell(2).GetString().Trim()=="元件位置" && row.Cell(3).GetString().Trim()=="盲点数量")
            {
                int start=row.RowNumber()+1,end=start;
                while(s.Cell(end,1).TryGetValue<double>(out _) && !s.Cell(end,2).IsEmpty())end++;
                return (s,start,end-start);
            }
        throw new InvalidDataException("模板未找到：元件位置 / 盲点数量表头");
    }
    public void Export(Analysis a,string template,string target,ExportOptions options)
    {
        if(a.Pending>0&&!options.Draft)throw new InvalidOperationException($"还有 {a.Pending} 条待确认记录，不能正式导出");
        if(Path.GetFullPath(target)==Path.GetFullPath(template)||Path.GetFullPath(target)==a.SourcePath)throw new InvalidOperationException("不能覆盖源文件或模板");
        using var wb=new XLWorkbook(template);
        var (s,start,oldCount)=LocateTemplate(wb);
        if(oldCount==0)throw new InvalidDataException("模板至少需要一条示范明细行，以便继承格式");
        var groups=AnalysisEngine.Group(a.Components.Where(c=>c.State==TestState.Untestable));
        var order=Enumerable.Range(start,oldCount).Select((r,i)=>new {Key=(s.Cell(r,4).GetFormattedString(),NormalizeReason(s.Cell(r,5).GetString())),Index=i,Row=r}).ToList();
        groups=groups.OrderBy(g=>order.FirstOrDefault(o=>o.Key==(g.Value,NormalizeReason(cfg.ReasonText(g.ReasonCode))))?.Index??int.MaxValue)
            .ThenBy(g=>Array.IndexOf(cfg.Reasons.Keys.ToArray(),g.ReasonCode)).ThenBy(g=>g.Value,StringComparer.Ordinal).ToList();
        // 保存样式到临时工作表，避免插删行影响来源行。
        var styles=wb.AddWorksheet("__styles_"+Guid.NewGuid().ToString("N")[..8]);
        s.Range(start,1,start+oldCount-1,6).CopyTo(styles.Cell(1,1));
        var heights=Enumerable.Range(start,oldCount).Select(r=>s.Row(r).Height).ToArray();
        int oldLast=s.LastRowUsed()!.RowNumber(), delta=groups.Count-oldCount;
        if(delta>0)s.Row(start+oldCount).InsertRowsAbove(delta);
        if(delta<0)s.Rows(start+groups.Count,start+oldCount-1).Delete();
        for(int i=0;i<groups.Count;i++)
        {
            var g=groups[i]; int r=start+i;
            var match=order.FirstOrDefault(o=>o.Key==(g.Value,NormalizeReason(cfg.ReasonText(g.ReasonCode))));
            int styleIndex=match?.Index??Math.Min(oldCount-1,1);
            string templateNote=styles.Cell(styleIndex+1,6).GetString();
            styles.Range(styleIndex+1,1,styleIndex+1,6).CopyTo(s.Cell(r,1));
            s.Range(r,1,r,6).Clear(XLClearOptions.Contents);
            string refs=ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),options.WrapWidth);
            if(refs.Length>32767)throw new InvalidDataException("分组位号超过Excel单元格长度上限，请调整分组");
            s.Cell(r,1).Value=i+1; s.Cell(r,2).Value=refs; s.Cell(r,3).Value=g.Count;
            s.Cell(r,4).Value=g.Value; s.Cell(r,5).Value=cfg.ReasonText(g.ReasonCode);
            s.Cell(r,6).Value=string.Join("；",g.Items.Select(c=>string.IsNullOrEmpty(c.Note)?(string.IsNullOrEmpty(options.DefaultNote)?templateNote:options.DefaultNote):c.Note).Distinct());
            s.Cell(r,2).Style.Alignment.WrapText=true;
            double needed=(refs.Count(c=>c=='\n')+1)*(s.Cell(r,2).Style.Font.FontSize*1.3)+5;
            if(needed>409)throw new InvalidDataException("位号行超过Excel最大行高，请增加换行宽度或拆分分组后重试");
            s.Row(r).Height=Math.Max(heights[styleIndex],needed);
        }
        foreach(var row in s.RowsUsed().Where(r=>r.RowNumber()<start))
        foreach(var cell in row.CellsUsed())
        {
            string value=cell.GetString();
            if(value.Contains("光板编号"))cell.Value="光板编号:"+options.BoardNumber;
            else if(value.Contains("成品编号"))cell.Value="成品编号:"+options.ProductNumber;
            else if(value.Contains("PCBA总点数"))cell.Value=$"PCBA总点数: {a.Total} 不可测点数: {a.Untestable} 源测试覆盖率:{cfg.FormatCoverage(a.Coverage)}   值针率：{(string.IsNullOrWhiteSpace(options.NeedleRate)?"未提供":options.NeedleRate)}";
        }
        if(options.Draft)s.Cell(1,1).Value=s.Cell(1,1).GetString()+"（待审核）";
        s.PageSetup.PrintAreas.Clear(); s.PageSetup.PrintAreas.Add(1,1,oldLast+delta,6);
        styles.Delete(); SaveAtomic(wb,target);
        Verify(target,a,options.Draft);
    }
    public static void Verify(string path,Analysis a,bool draft=false)
    {
        using var wb=new XLWorkbook(path);var(s,start,count)=LocateTemplate(wb);
        var all=new List<string>();
        for(int r=start;r<start+count;r++)
        {
            var refs=SplitRefs(s.Cell(r,2).GetString()); all.AddRange(refs);
            if(s.Cell(r,3).GetValue<int>()!=refs.Length)throw new InvalidDataException($"导出回读：第 {r} 行数量不符");
        }
        var expected=a.Components.Where(c=>c.State==TestState.Untestable).Select(c=>c.Reference).Order().ToArray();
        if(!all.Order().SequenceEqual(expected))throw new InvalidDataException("导出回读：位号与源记录不一致");
        if(!draft&&all.Distinct().Count()!=all.Count)throw new InvalidDataException("导出回读：重复位号");
    }
    public static string[] SplitRefs(string s)=>s.Replace("\r","").Replace("\n","").Split('.',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    public List<AuditDifference> Compare(Analysis a,string template)
    {
        using var wb=new XLWorkbook(template);var(s,start,count)=LocateTemplate(wb);
        var output=new List<AuditDifference>();var entries=new List<(string Ref,string Value,string Reason)>();
        for(int r=start;r<start+count;r++)
        {
            var refs=SplitRefs(s.Cell(r,2).GetString());
            if(refs.Length!=s.Cell(r,3).GetValue<int>())output.Add(new("样表数量不符","",$"第 {r} 行：数量列 {s.Cell(r,3).GetString()}，实际位号 {refs.Length}"));
            entries.AddRange(refs.Select(x=>(x,s.Cell(r,4).GetFormattedString(),NormalizeReason(s.Cell(r,5).GetString()))));
        }
        var source=a.Components.Where(c=>c.State==TestState.Untestable).ToList();
        foreach(var c in source)
        {
            var found=entries.Where(e=>e.Ref==c.Reference).ToList();
            if(found.Count==0)output.Add(new("样表遗漏",c.Reference,$"源行 {c.SourceRow}"));
            else if(!found.Any(e=>e.Value==c.Value&&e.Reason==NormalizeReason(cfg.ReasonText(c.ReasonCode))))output.Add(new("样表分类差异",c.Reference,$"应为 {c.Value} / {cfg.ReasonText(c.ReasonCode)}"));
        }
        foreach(var e in entries.Where(e=>!source.Any(c=>c.Reference==e.Ref)))output.Add(new("样表多出",e.Ref,"不在源文件不可测记录中"));
        foreach(var g in entries.GroupBy(e=>e.Ref).Where(g=>g.Count()>1))output.Add(new("样表重复",g.Key,$"出现 {g.Count()} 次"));
        return output;
    }
    public void ExportAudit(Analysis a,string target,string? template=null)
    {
        using var wb=new XLWorkbook();var s=wb.AddWorksheet("逐点核对");
        string[] heads=["源工作表","源行","原始位号","规范位号","元件类型","原标准值","显示标准值","ICT缺件符号","黄色底纹","源检测状态","输出检测状态","人工已确认","盲点原因（未审为建议）","选用规则","全部候选规则与来源","问题","备注","测试程序证据","规则版本"];
        for(int c=0;c<heads.Length;c++)s.Cell(1,c+1).Value=heads[c];
        for(int i=0;i<a.Components.Count;i++)
        {
            var c=a.Components[i];string[] values=[c.Sheet,c.SourceRow.ToString(),c.RawReference,c.Reference,c.Type,c.RawValue,c.Value,c.StatusSymbol,c.Yellow?"是":"否",c.SourceState.ToString(),c.State.ToString(),c.ManuallyReviewed?"是":"否",cfg.ReasonText(c.ReasonCode),c.RuleId,System.Text.Json.JsonSerializer.Serialize(c.MatchedRules,RuleConfig.JsonOptions),string.Join("；",c.Issues.Select(x=>x.Message)),c.Note,c.ProgramEvidence,a.RuleVersion];
            for(int col=0;col<values.Length;col++)s.Cell(i+2,col+1).Value=values[col];
        }
        var summary=wb.AddWorksheet("统计");
        var stats=new (string,double)[]{("源记录",a.Total),("源可测",a.Testable),("源不可测",a.SourceUntestable),("待确认",a.Pending),("源精确覆盖率",a.Coverage),("输出不可测",a.Untestable),("输出可测",a.OutputTestable),("输出覆盖率",a.OutputCoverage)};
        for(int i=0;i<stats.Length;i++){summary.Cell(i+1,1).Value=stats[i].Item1;summary.Cell(i+1,2).Value=stats[i].Item2;}summary.Cell(5,2).Style.NumberFormat.Format="0.00000000%";
        summary.Cell(8,2).Style.NumberFormat.Format="0.00000000%";
        summary.Cell(9,1).Value="源表可测统计";summary.Cell(9,2).Value=a.ReportTestable?.ToString()??"未提供";
        summary.Cell(10,1).Value="源表不可测统计";summary.Cell(10,2).Value=a.ReportUntestable?.ToString()??"未提供";
        summary.Cell(11,1).Value="源表覆盖率";summary.Cell(11,2).Value=a.ReportCoverage?.ToString("P8")??"未提供";
        summary.Cell(12,1).Value="源统计核对异常";summary.Cell(12,2).Value=string.Join("；",a.Issues.Select(i=>i.Message));
        int rr=14;foreach(var g in a.Components.Where(c=>c.State==TestState.Untestable).GroupBy(c=>c.ReasonCode)){summary.Cell(rr,1).Value=cfg.ReasonText(g.Key);summary.Cell(rr++,2).Value=g.Count();}
        var diff=wb.AddWorksheet("样表差异");diff.Cell(1,1).Value="类别";diff.Cell(1,2).Value="位号";diff.Cell(1,3).Value="说明";
        if(!string.IsNullOrEmpty(template)) {int r=2;foreach(var d in Compare(a,template)){diff.Cell(r,1).Value=d.Kind;diff.Cell(r,2).Value=d.Reference;diff.Cell(r++,3).Value=d.Detail;}}
        var log=wb.AddWorksheet("人工修改日志"); string[] lh=["时间","源行","修改前","修改后","理由","修改人","规则版本"];for(int c=0;c<lh.Length;c++)log.Cell(1,c+1).Value=lh[c];
        for(int i=0;i<a.Edits.Count;i++){var e=a.Edits[i];log.Cell(i+2,1).Value=e.Time.ToString("O");log.Cell(i+2,2).Value=e.SourceRow;log.Cell(i+2,3).Value=e.Before;log.Cell(i+2,4).Value=e.After;log.Cell(i+2,5).Value=e.Reason;log.Cell(i+2,6).Value=e.Reviewer;log.Cell(i+2,7).Value=e.RuleVersion;}
        foreach(var sheet in wb.Worksheets){sheet.Row(1).Style.Font.Bold=true;sheet.SheetView.FreezeRows(1);sheet.ColumnsUsed().Width=22;}
        SaveAtomic(wb,target);
    }
    private static void SaveAtomic(XLWorkbook wb,string target)
    {
        var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!,"."+Guid.NewGuid()+".xlsx");
        try {wb.SaveAs(temp);File.Move(temp,target,true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){throw new IOException("无法写入输出文件，请关闭占用该文件的Excel/WPS并检查目录权限。",ex);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
