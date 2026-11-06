using NPOI.SS.UserModel;
using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using System.Text.RegularExpressions;

namespace ICTCoverage.Core;

public sealed class CoverageReader
{
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z]+\d+(?:[_/].*)?$", RegexOptions.Compiled);
    private static string Text(ICell? c) => c == null ? "" : new DataFormatter(System.Globalization.CultureInfo.InvariantCulture).FormatCellValue(c).Trim();
    private static string MergedText(ISheet s, int r, int c)
    {
        foreach (var m in s.MergedRegions)
            if (m.IsInRange(r,c)) return Text(s.GetRow(m.FirstRow)?.GetCell(m.FirstColumn));
        return Text(s.GetRow(r)?.GetCell(c));
    }
    public List<FieldMap> Locate(string path)
    {
        using var stream = File.OpenRead(path);
        using var wb = WorkbookFactory.Create(stream);
        var result = new List<FieldMap>();
        for (int si=0; si<wb.NumberOfSheets; si++)
        {
            var s=wb.GetSheetAt(si);
            for (int r=0;r<=Math.Min(s.LastRowNum,100);r++)
            {
                var row=s.GetRow(r); if(row==null) continue;
                for(int c=0;c<row.LastCellNum;c++)
                {
                    if(Text(row.GetCell(c))!="缺件") continue;
                    if(!Enumerable.Range(Math.Max(0,r-4),Math.Min(4,r)).Any(h=>MergedText(s,h,c).Equals("ICT",StringComparison.OrdinalIgnoreCase))) continue;
                    int maxCol=Math.Max(1,(int)row.LastCellNum);
                    int rc=Enumerable.Range(0,maxCol).OrderByDescending(col=>Enumerable.Range(r+1,Math.Max(0,Math.Min(150,s.LastRowNum-r))).Count(rr=>ReferencePattern.IsMatch(Text(s.GetRow(rr)?.GetCell(col))))).First();
                    int vc=-1;
                    for(int h=Math.Max(0,r-4);h<=r;h++)
                    for(int col=0;col<maxCol;col++)
                        if(new[]{"标准值","標準值","元件值","Value","RISK"}.Contains(Text(s.GetRow(h)?.GetCell(col)),StringComparer.OrdinalIgnoreCase)) vc=col;
                    if(vc<0) continue; // 不猜测标准值列
                    result.Add(new(s.SheetName,r,rc,vc,c));
                }
            }
        }
        return result.Distinct().ToList();
    }
    public Analysis Read(string path, FieldMap map)
    {
        using var stream=File.OpenRead(path); using var wb=WorkbookFactory.Create(stream);
        var s=wb.GetSheet(map.Sheet) ?? throw new InvalidDataException("工作表不存在");
        var a=new Analysis { SourcePath=Path.GetFullPath(path) };
        var ids=Regex.Match(Path.GetFileName(path),@"(?<board>MG[A-Za-z0-9]+)-(?<product>P\d+)");
        if(ids.Success) { a.BoardNumber=ids.Groups["board"].Value; a.ProductNumber=ids.Groups["product"].Value; }
        for(int r=0;r<=map.HeaderRow;r++)
        {
            var header=s.GetRow(r);if(header==null)continue;
            var texts=header.Cells.Select(Text).ToArray();
            var model=texts.Select(t=>Regex.Match(t,@"Model\s*Name\s*[:：]\s*(\S+)",RegexOptions.IgnoreCase)).FirstOrDefault(m=>m.Success);
            if(model!=null)a.ProductNumber=model.Groups[1].Value;
            var number=header.GetCell(map.StatusColumn);
            if(number==null || !(number.CellType==CellType.Numeric || number.CellType==CellType.Formula&&number.CachedFormulaResultType==CellType.Numeric))continue;
            if(texts.Any(t=>t.Contains("Coverage",StringComparison.OrdinalIgnoreCase)&&t.Contains('%')))a.ReportCoverage=number.NumericCellValue;
            if(texts.Any(t=>t is "√" or "✓"))a.ReportTestable=(int)number.NumericCellValue;
            if(texts.Any(t=>t is "Х" or "X" or "×" or "✕"))a.ReportUntestable=(int)number.NumericCellValue;
        }
        for(int r=map.HeaderRow+1;r<=s.LastRowNum;r++)
        {
            var row=s.GetRow(r); if(row==null) continue;
            string raw=Text(row.GetCell(map.ReferenceColumn)), value=Text(row.GetCell(map.ValueColumn)), status=Text(row.GetCell(map.StatusColumn));
            if(string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(status)) continue;
            if(new[]{"合计","合計","总计","總計","备注","備註"}.Any(x=>raw.StartsWith(x))) break;
            var item=new Component { SourceRow=r+1, Sheet=s.SheetName, RawReference=raw, RawValue=value, StatusSymbol=status,
                Yellow=new[]{map.ReferenceColumn,map.ValueColumn,map.StatusColumn}.Any(col=>IsYellow(wb,row.GetCell(col))) };
            a.Components.Add(item);
        }
        if(a.Total==0) throw new InvalidDataException("选定范围内没有元件记录");
        return a;
    }
    private static bool IsYellow(IWorkbook wb, ICell? cell)
    {
        if(cell==null || cell.CellStyle.FillPattern==FillPattern.NoFill) return false;
        if(wb is HSSFWorkbook h) {
            var rgb=h.GetCustomPalette().GetColor(cell.CellStyle.FillForegroundColor)?.GetTriplet();
            return rgb is {Length:>=3} && rgb[0]>=220 && rgb[1]>=200 && rgb[2]<=100;
        }
        if(cell.CellStyle is XSSFCellStyle x) {
            var rgb=x.FillForegroundXSSFColor?.RGB;
            return rgb is {Length:>=3} && rgb[^3]>=220 && rgb[^2]>=200 && rgb[^1]<=100;
        }
        return false;
    }
}
