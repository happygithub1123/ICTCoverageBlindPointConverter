using ICTCoverage.Core;
namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    // Stable page keys keep routing and persisted state independent of menu wording.
    static readonly (string Key,string Label)[] NavigationItems=
    [
        ("分类筛选","分类筛选"),("复制","复制整理"),("异常待确认","待确认项"),
        ("盲点设置","盲点设置"),("类型分类设置","分类设置"),("规则设置","规则设置"),
        ("规则总览","规则总览"),("人工修改日志","修改记录"),("使用说明","使用说明")
    ];
    readonly Label pendingBadge=new(){Name="PendingBadge",Dock=DockStyle.Right,Width=58,ForeColor=Color.FromArgb(255,190,90),BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold),TextAlign=ContentAlignment.MiddleCenter,Visible=false,Cursor=Cursors.Hand};
    static string PageLabel(string key)=>NavigationItems.First(p=>p.Key==key).Label;
    void UpdatePendingBadge(int count)
    {
        pendingBadge.Text=count>9999?"9999+":count.ToString();pendingBadge.AccessibleName=$"{count}条待确认记录";pendingBadge.Visible=count>0;
    }
    Control BuildNav()
    {
        var outer=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=AppTheme.Navy,ColumnCount=1,RowCount=4,Margin=new Padding(3)};
        outer.RowStyles.Add(new(SizeType.Percent,100));outer.RowStyles.Add(new(SizeType.Absolute,124));outer.RowStyles.Add(new(SizeType.Absolute,124));outer.RowStyles.Add(new(SizeType.Absolute,105));
        var host=new Panel{Name="NavigationScroll",Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};outer.Controls.Add(host,0,0);
        var menu=new TableLayoutPanel{Name="NavigationMenu",Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=12,Padding=new Padding(12,8,12,8),Margin=Padding.Empty};menu.ColumnStyles.Add(new(SizeType.Percent,100));host.Controls.Add(menu);
        menu.RowStyles.Add(new(SizeType.Absolute,100));
        var brand=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Padding=new Padding(4,12,4,0),Margin=Padding.Empty};
        using(var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.png"))if(stream!=null)
        {
            using var source=new Bitmap(stream);var picture=new PictureBox{Image=new Bitmap(source),Width=60,Height=60,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,0,8,0)};
            picture.Disposed+=(_,_)=>picture.Image?.Dispose();brand.Controls.Add(picture);
        }
        brand.Controls.Add(new Label{Text="ICT盲点表工具\nWHAPPY",AutoSize=true,ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",15.5f,FontStyle.Bold),Margin=new Padding(0,7,0,0)});menu.Controls.Add(brand,0,0);
        int row=1;
        for(int i=0;i<NavigationItems.Length;i++)
        {
            if(i is 3 or 8){menu.RowStyles.Add(new(SizeType.Absolute,12));row++;}
            var item=NavigationItems[i];menu.RowStyles.Add(new(SizeType.Absolute,42));
            var button=AppTheme.Button(item.Label,AppTheme.Navy,(_,_)=>ShowPage(item.Key));button.AutoSize=false;button.MinimumSize=new Size(0,32);button.Margin=new Padding(2,3,2,3);button.Padding=new Padding(12,2,8,2);button.Dock=DockStyle.Fill;button.Font=new Font("Microsoft YaHei UI",13.5f,i<3?FontStyle.Bold:FontStyle.Regular);button.TextAlign=ContentAlignment.MiddleLeft;
            button.FlatAppearance.MouseOverBackColor=Color.FromArgb(43,61,88);button.FlatAppearance.MouseDownBackColor=AppTheme.Blue;
            nav[item.Key]=button;menu.Controls.Add(button,0,row++);
            if(item.Key=="异常待确认"){button.Controls.Add(pendingBadge);pendingBadge.Click+=(_,_)=>ShowPage(item.Key);}
        }
        var kinds=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3};kinds.ColumnStyles.Add(new(SizeType.Percent,50));kinds.ColumnStyles.Add(new(SizeType.Percent,50));for(int i=0;i<3;i++)kinds.RowStyles.Add(new(SizeType.Percent,33.33f));
        allInspection=SideKindButton("全部检测项目",AppTheme.Blue,()=>ChooseInspection(null));kinds.Controls.Add(allInspection,0,0);kinds.SetColumnSpan(allInspection,2);Color[] colors=[AppTheme.Blue,Color.FromArgb(124,82,193),Color.FromArgb(11,145,151),AppTheme.Orange];
        foreach(var kind in Enum.GetValues<ICTCoverage.Core.InspectionKind>()){var button=SideKindButton(kind.Name(),colors[(int)kind],()=>ChooseInspection(kind));inspectionButtons[kind]=button;kinds.Controls.Add(button,(int)kind%2,1+(int)kind/2);}outer.Controls.Add(kinds,0,1);
        sidebarStats.Padding=new Padding(15,0,0,0);outer.Controls.Add(sidebarStats,0,2);
        var credit=new Label{Name="DesignerCredit",Text="Designed by WHAPPY\n设计人员：王开心\nWANG KAIXIN · v0.5.5\n本软件最终解释权归王开心",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(208,218,235),Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold),Padding=new Padding(15,4,0,4),TextAlign=ContentAlignment.MiddleLeft};outer.Controls.Add(credit,0,3);return outer;
    }
}
