using System.Text;
using System.Net;
using ICTCoverage.Core;

namespace ICTCoverage.WinForms;

public static class ClipboardService
{
    public static string BuildHtml(string[] cells)
    {
        string fragment="<table><tr>"+string.Concat(cells.Select(c=>"<td style=\"mso-number-format:'\\@';white-space:pre-wrap\">"+WebUtility.HtmlEncode(c).Replace("\n","<br style=\"mso-data-placement:same-cell\">")+"</td>"))+"</tr></table>";
        const string prefix="<html><body><!--StartFragment-->";
        const string suffix="<!--EndFragment--></body></html>";
        const string header="Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\nStartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n";
        int start=Encoding.UTF8.GetByteCount(string.Format(header,0,0,0,0));
        int fragmentStart=start+Encoding.UTF8.GetByteCount(prefix),fragmentEnd=fragmentStart+Encoding.UTF8.GetByteCount(fragment);
        return string.Format(header,start,fragmentEnd+Encoding.UTF8.GetByteCount(suffix),fragmentStart,fragmentEnd)+prefix+fragment+suffix;
    }
    public static void Copy(string[] cells)
    {
        var data=new DataObject();
        string text=cells.Length==1?cells[0]:string.Join("\t",cells.Select(c=>c.Contains('\n')||c.Contains('\t')||c.Contains('"')?"\""+c.Replace("\"","\"\"")+"\"":c));
        data.SetData(DataFormats.UnicodeText,text.Replace("\n","\r\n"));
        data.SetData(DataFormats.Html,BuildHtml(cells));
        Clipboard.SetDataObject(data,true,5,100);
    }
    public static string[] GroupCells(BlindGroup group,RuleConfig cfg,int width,bool fourColumns)
    {
        if(group.Pending)throw new InvalidOperationException("本组存在待确认记录，请先处理后再复制填表");
        string refs=ReferenceFormatter.Format(group.Items.Select(c=>c.Reference),width);
        return fourColumns?[refs,group.Count.ToString(),group.Value,cfg.ReasonText(group.ReasonCode)]:[refs];
    }
}
