namespace ICTCoverage.WinForms;

internal sealed class OverviewCard : Panel
{
    readonly Label caption,value;readonly Color accent;bool selected;
    public OverviewCard(string text,Color color,Action click)
    {
        accent=color;Dock=DockStyle.Fill;BackColor=Color.White;Margin=new Padding(4);Padding=new Padding(10,5,8,3);Cursor=Cursors.Hand;AccessibleName=text;
        caption=new Label{Text=text,Dock=DockStyle.Top,Height=22,ForeColor=AppTheme.Muted,Font=new Font("Microsoft YaHei UI",10.5f)};
        value=new Label{Text="—",Dock=DockStyle.Fill,ForeColor=color,Font=new Font("Microsoft YaHei UI",22,FontStyle.Bold)};Controls.Add(value);Controls.Add(caption);
        foreach(Control c in new Control[]{this,caption,value})c.Click+=(_,_)=>click();
        Paint+=(_,e)=>{using var pen=new Pen(selected?accent:Color.FromArgb(228,234,243),selected?2:1);e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);};
    }
    public void Update(int? count,bool active)
    {
        selected=active;value.Text=count?.ToString()??"—";caption.ForeColor=active?accent:AppTheme.Muted;Enabled=count.HasValue;AccessibleDescription=value.Text;Invalidate();
    }
}
