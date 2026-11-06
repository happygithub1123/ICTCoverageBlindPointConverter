using ICTCoverage.Core;

namespace ICTCoverage.WinForms;

internal sealed class InspectionCard : Panel
{
    readonly Label title,percent;readonly LinkLabel yes,no,na;readonly ToolTip hint=new();readonly Color accent;
    public InspectionKind Kind { get; }
    public bool Selected { get; set; }
    public InspectionCard(InspectionKind kind,Color color,Action<InspectionKind,TestState?> choose)
    {
        Kind=kind;accent=color;Dock=DockStyle.Fill;Height=68;Margin=new Padding(4);Padding=new Padding(8,3,8,3);BackColor=Color.White;Cursor=Cursors.Hand;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};layout.ColumnStyles.Add(new(SizeType.Percent,50));layout.ColumnStyles.Add(new(SizeType.Percent,50));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,25));Controls.Add(layout);
        title=new Label{Text=kind.Name(),Dock=DockStyle.Fill,ForeColor=color,Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
        percent=new Label{Text="—",Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",17,FontStyle.Bold),ForeColor=color,TextAlign=ContentAlignment.MiddleRight};layout.Controls.Add(title,0,0);layout.Controls.Add(percent,1,0);
        var counts=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};for(int i=0;i<3;i++)counts.ColumnStyles.Add(new(SizeType.Percent,33.33f));
        yes=Count("√ —",AppTheme.Green);no=Count("Х —",AppTheme.Blue);na=Count("∕ —",AppTheme.Muted);counts.Controls.Add(yes,0,0);counts.Controls.Add(no,1,0);counts.Controls.Add(na,2,0);layout.Controls.Add(counts,0,1);layout.SetColumnSpan(counts,2);
        foreach(Control c in new Control[]{this,layout,title,percent})c.Click+=(_,_)=>choose(kind,null);yes.LinkClicked+=(_,_)=>choose(kind,TestState.Testable);no.LinkClicked+=(_,_)=>choose(kind,TestState.Untestable);na.LinkClicked+=(_,_)=>choose(kind,TestState.NotApplicable);
        Paint+=(_,e)=>{using var pen=new Pen(Selected?accent:Color.FromArgb(228,234,243),Selected?2:1);e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);};
    }
    static LinkLabel Count(string text,Color color)=>new(){Text=text,Dock=DockStyle.Fill,AutoSize=false,LinkColor=color,Font=new Font("Microsoft YaHei UI",10),LinkBehavior=LinkBehavior.HoverUnderline,TextAlign=ContentAlignment.MiddleLeft};
    public void Update(InspectionStatistics? stats,bool selected)
    {
        Selected=selected;Enabled=stats!=null;percent.Text=stats?.Percentage??"未提供";yes.Text="√ "+(stats?.Testable.ToString()??"—");no.Text="Х "+(stats?.Untestable.ToString()??"—");na.Text="∕ "+(stats?.NotApplicable.ToString()??"—");
        string text=stats==null?"报告未提供此检测列":$"统计范围 {stats.Total} 个元件；覆盖率 {(stats.Coverage.HasValue?stats.Coverage.Value.ToString("P6"):"—")}；未知状态 {stats.Unknown}";
        foreach(Control c in new Control[]{this,title,percent,yes,no,na})hint.SetToolTip(c,text);Invalidate();
    }
    protected override void Dispose(bool disposing){if(disposing)hint.Dispose();base.Dispose(disposing);}
}
