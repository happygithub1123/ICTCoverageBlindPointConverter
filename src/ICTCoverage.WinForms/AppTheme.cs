namespace ICTCoverage.WinForms;
internal static class AppTheme
{
    public static readonly Color Background=Color.FromArgb(243,246,251),Ink=Color.FromArgb(28,40,61),Muted=Color.FromArgb(104,117,137),Blue=Color.FromArgb(44,104,224),Green=Color.FromArgb(22,146,104),Orange=Color.FromArgb(220,133,24),Yellow=Color.FromArgb(250,220,78),Navy=Color.FromArgb(26,39,63);
    public static Button Button(string text,Color color,EventHandler action)
    {
        var b=new Button{Text=text,BackColor=color,ForeColor=color==Yellow?Ink:Color.White,FlatStyle=FlatStyle.Flat,AutoSize=true,MinimumSize=new Size(110,38),Padding=new Padding(10,4,10,4),Margin=new Padding(5),Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;b.Click+=action;return b;
    }
    public static DataGridView Grid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,GridColor=Color.FromArgb(235,239,245),EnableHeadersVisualStyles=false,ColumnHeadersHeight=40,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,ColumnHeadersDefaultCellStyle=new(){BackColor=Color.FromArgb(232,238,248),ForeColor=Ink,Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold),SelectionBackColor=Color.FromArgb(232,238,248)},DefaultCellStyle=new(){BackColor=Color.White,ForeColor=Ink,SelectionBackColor=Color.FromArgb(219,233,255),SelectionForeColor=Ink,Padding=new Padding(5)},AlternatingRowsDefaultCellStyle=new(){BackColor=Color.FromArgb(249,251,254)},RowTemplate={Height=36}};
    public static FlowLayoutPanel Flow()=>new(){Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(2)};
    public static Label Label(string text,int size=10,bool bold=false)=>new(){Text=text,AutoSize=true,ForeColor=Ink,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(5,10,5,5)};
}
