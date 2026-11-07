namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    Control BuildHelp()
    {
        using var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.UserGuide.md")??throw new InvalidOperationException("使用说明资源缺失");
        using var reader=new StreamReader(stream);
        var topics=reader.ReadToEnd().Replace("\r\n","\n").Split("\n## ").Skip(1).Select(part=>{int end=part.IndexOf('\n');return(Title:part[..end].Trim(),Body:part[(end+1)..].Trim());}).ToArray();
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};panel.ColumnStyles.Add(new(SizeType.Percent,100));panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.Controls.Add(AppTheme.Label("离线使用手册 · 先看完整例子，再按菜单查阅",12,true),0,0);
        var select=new ComboBox{Name="HelpTopics",Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Font=new Font("Microsoft YaHei UI",12),Margin=new Padding(5,8,5,14)};
        var body=new RichTextBox{Name="HelpBody",Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=AppTheme.Ink,Font=new Font("Microsoft YaHei UI",12),DetectUrls=false,WordWrap=true,ScrollBars=RichTextBoxScrollBars.Vertical};
        panel.Controls.Add(select,0,1);panel.Controls.Add(body,0,2);select.Items.AddRange(topics.Select(t=>(object)t.Title).ToArray());
        select.SelectedIndexChanged+=(_,_)=>{body.Text=topics[select.SelectedIndex].Title+"\n\n"+topics[select.SelectedIndex].Body;body.Select(0,topics[select.SelectedIndex].Title.Length);using var heading=new Font(body.Font.FontFamily,16,FontStyle.Bold);body.SelectionFont=heading;body.SelectionColor=AppTheme.Blue;body.Select(0,0);body.ScrollToCaret();};select.SelectedIndex=0;return panel;
    }
}
