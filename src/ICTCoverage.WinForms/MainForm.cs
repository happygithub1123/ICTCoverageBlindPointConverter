using ICTCoverage.Core;
using System.Text.Json;
using System.Security.Cryptography;
namespace ICTCoverage.WinForms;

public sealed partial class MainForm : Form
{
    readonly string userDir;
    string RulePath=>Path.Combine(userDir,"rules-v4.json");
    readonly string defaults=Path.Combine(AppContext.BaseDirectory,"config","rules.json");
    RuleConfig cfg; Analysis? analysis; CoverageReport? report; bool busy,refreshing;
    readonly TextBox input=new(){Dock=DockStyle.Fill,ReadOnly=true,PlaceholderText="选择或拖入 Coverage 报告（.xls / .xlsx）"},refFilter=new(){Width=185,PlaceholderText="搜索位号"},valueFilter=new(){Width=145,PlaceholderText="标准值"},reasonFilter=new(){Width=175,PlaceholderText="盲点原因"},board=new(){Width=150},product=new(){Width=150},needle=new(){Width=85},note=new(){Width=155,PlaceholderText="留空沿用模板"};
    readonly ComboBox type=new(){Width=120,DropDownStyle=ComboBoxStyle.DropDownList},inspection=new(){Width=90,DropDownStyle=ComboBoxStyle.DropDownList},stateFilter=new(){Width=105,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly Dictionary<InspectionKind,InspectionCard> cards=[];
    readonly Label sidebarStats=new(){Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",9),TextAlign=ContentAlignment.MiddleLeft},scopeStats=AppTheme.Label("导入报告后显示四项检测统计",9),projectInfo=AppTheme.Label("项目：自动识别",9);
    readonly NumericUpDown width=new(){Minimum=20,Maximum=150,Value=48,Width=65};
    readonly DataGridView grid=AppTheme.Grid(),issues=AppTheme.Grid(),rulesGrid=AppTheme.Grid(),ruleList=AppTheme.Grid(),log=AppTheme.Grid();
    readonly TextBox refs=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",11),BorderStyle=BorderStyle.None,BackColor=Color.White};
    readonly Label status=AppTheme.Label("按 ICT 检测项目筛选；打 Х 的元件纳入盲点表，√ 和 ∕ 排除。",9),title=AppTheme.Label("分类筛选 / 复制",19,true);
    readonly Panel content=new(){Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12)};
    readonly Dictionary<string,Control> pages=[]; readonly Dictionary<string,Button> nav=[];
    readonly CheckBox archived=new(){Text="显示已删除规则",AutoSize=true,Margin=new Padding(12,15,0,0)};
    readonly Button copyOne,copyFour;
    List<BlindGroup> groups=[];
    public MainForm(string? dataDirectory=null)
    {
        userDir=dataDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ICTCoverageBlindPointConverter");Directory.CreateDirectory(userDir);
        if(!File.Exists(RulePath)){
            string old=Path.Combine(userDir,"rules-v3.json");if(!File.Exists(old))old=Path.Combine(userDir,"rules-v2.json");
            try{var c=RuleManagementService.Upgrade(RuleConfig.Load(File.Exists(old)?old:defaults),RuleConfig.Load(defaults));c.NotApplicableSymbols=["∕","/","／"];RuleManagementService.Save(c,RulePath);}catch{File.Copy(defaults,RulePath);}

        }
        try{cfg=RuleConfig.Load(RulePath);}catch{File.Copy(RulePath,RulePath+".invalid-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true);cfg=RuleConfig.Load(defaults);RuleManagementService.Save(cfg,RulePath);}
        var bundled=RuleConfig.Load(defaults);
        if(System.Version.TryParse(cfg.Version,out var previous)&&System.Version.TryParse(bundled.Version,out var latest)&&previous<latest){File.Copy(RulePath,RulePath+".before-v0.4.1",true);cfg=RuleManagementService.Upgrade(cfg,bundled);RuleManagementService.Save(cfg,RulePath);}
        Text="ICT 盲点表工具 · WHAPPY";Font=new Font("Microsoft YaHei UI",10);BackColor=AppTheme.Background;ForeColor=AppTheme.Ink;Width=1440;Height=960;MinimumSize=new Size(1120,760);AutoScaleMode=AutoScaleMode.Dpi;StartPosition=FormStartPosition.CenterScreen;
        using(var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.ico"))if(stream!=null)Icon=new Icon(stream);
        copyOne=AppTheme.Button("复制本组位号",AppTheme.Blue,(_,_)=>CopyGroup(false));copyFour=AppTheme.Button("复制本组四列",AppTheme.Green,(_,_)=>CopyGroup(true));
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};root.ColumnStyles.Add(new(SizeType.Absolute,220));root.ColumnStyles.Add(new(SizeType.Percent,100));Controls.Add(root);root.Controls.Add(BuildNav(),0,0);
        var work=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(18,8,18,8)};for(int i=0;i<3;i++)work.RowStyles.Add(new(SizeType.AutoSize));work.RowStyles.Add(new(SizeType.Percent,100));work.RowStyles.Add(new(SizeType.AutoSize));root.Controls.Add(work,1,0);
        work.Controls.Add(title,0,0);work.Controls.Add(BuildImport(),0,1);work.Controls.Add(BuildStats(),0,2);work.Controls.Add(content,0,3);work.Controls.Add(status,0,4);
        AddPage("分类筛选 / 复制",BuildPreview());AddPage("异常 / 待确认",BuildIssues());AddPage("盲点设置",BuildReasons());AddPage("规则设置",BuildRules());AddPage("V2规则总览",BuildRuleOverview());AddPage("人工修改日志",log);RefreshChoices();ShowPage("分类筛选 / 复制");
        grid.SelectionChanged+=(_,_)=>ShowGroup();type.SelectedIndexChanged+=(_,_)=>{if(!refreshing)RefreshResults();};foreach(var f in new[]{refFilter,valueFilter,reasonFilter})f.TextChanged+=(_,_)=>RefreshResults();width.ValueChanged+=(_,_)=>ShowGroup();archived.CheckedChanged+=(_,_)=>RefreshRules();inspection.SelectedIndexChanged+=(_,_)=>{if(!refreshing)ActivateInspection();};stateFilter.SelectedIndexChanged+=(_,_)=>{if(!refreshing)RefreshResults();};needle.TextChanged+=(_,_)=>RefreshSidebar();
        AllowDrop=true;DragEnter+=(_,e)=>{if(e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};DragDrop+=async(_,e)=>{if(!busy&&e.Data?.GetData(DataFormats.FileDrop) is string[] p&&p.Length>0){input.Text=p[0];await ReadAsync();}};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;return;}SaveSession();};
    }
    Control BuildNav()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=AppTheme.Navy,ColumnCount=1,RowCount=9,Padding=new Padding(12,18,12,12)};p.RowStyles.Add(new(SizeType.Absolute,155));for(int i=0;i<6;i++)p.RowStyles.Add(new(SizeType.Absolute,46));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.Absolute,85));
        var brand=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};using(var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.png"))if(stream!=null)brand.Controls.Add(new PictureBox{Image=new Bitmap(stream),Width=68,Height=68,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(8,0,0,8)});
        brand.Controls.Add(new Label{Text="ICT 盲点表工具",AutoSize=true,ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold)});brand.Controls.Add(new Label{Text="Coverage → Blind Points",AutoSize=true,ForeColor=Color.FromArgb(174,190,216),Margin=new Padding(4,8,0,0)});p.Controls.Add(brand,0,0);
        string[] names=["分类筛选 / 复制","异常 / 待确认","盲点设置","规则设置","V2规则总览","人工修改日志"];
        for(int i=0;i<names.Length;i++){string name=names[i];var b=AppTheme.Button(name,AppTheme.Navy,(_,_)=>ShowPage(name));b.Dock=DockStyle.Fill;b.TextAlign=ContentAlignment.MiddleLeft;b.Font=new Font(Font,i<2?FontStyle.Bold:FontStyle.Regular);nav[name]=b;p.Controls.Add(b,0,i+1);}
        p.Controls.Add(sidebarStats,0,7);
        p.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Designed by WHAPPY\n设计人员：王开心\nWANG KAIXIN  ·  v0.4.1",ForeColor=Color.FromArgb(208,218,235),Font=new Font("Microsoft YaHei UI",9),TextAlign=ContentAlignment.BottomLeft},0,8);return p;
    }
    Control BuildImport()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=3};var row=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4};row.ColumnStyles.Add(new(SizeType.Percent,100));for(int i=0;i<3;i++)row.ColumnStyles.Add(new(SizeType.AutoSize));input.Margin=new Padding(4,12,8,4);row.Controls.Add(input,0,0);
        row.Controls.Add(AppTheme.Button("导入报告",AppTheme.Blue,async(_,_)=>{using var d=new OpenFileDialog{Filter="Coverage报告|*.xls;*.xlsx"};if(d.ShowDialog(this)==DialogResult.OK){input.Text=d.FileName;await ReadAsync();}}),1,0);row.Controls.Add(AppTheme.Button("读取分析",AppTheme.Blue,async(_,_)=>await ReadAsync()),2,0);row.Controls.Add(AppTheme.Button("导出盲点表",AppTheme.Green,async(_,_)=>await ExportAsync()),3,0);p.Controls.Add(row,0,0);
        var settings=AppTheme.Flow();settings.Visible=false;settings.Controls.AddRange([AppTheme.Label("光板编号"),board,AppTheme.Label("成品编号"),product,AppTheme.Label("值针率"),needle,AppTheme.Label("备注"),note,AppTheme.Label("换行宽度"),width]);var toggle=new LinkLabel{Text="输出设置 ▾",AutoSize=true,LinkColor=AppTheme.Muted,Margin=new Padding(5,3,0,6)};toggle.LinkClicked+=(_,_)=>{settings.Visible=!settings.Visible;toggle.Text=settings.Visible?"收起输出设置 ▴":"输出设置 ▾";};p.Controls.Add(toggle,0,1);p.Controls.Add(settings,0,2);return p;
    }
    Control BuildStats()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4,RowCount=2,Padding=new Padding(0,3,0,7)};p.RowStyles.Add(new(SizeType.Absolute,132));p.RowStyles.Add(new(SizeType.AutoSize));
        Color[] colors=[AppTheme.Blue,Color.FromArgb(133,85,201),Color.FromArgb(16,150,167),AppTheme.Orange];
        foreach(var kind in Enum.GetValues<InspectionKind>()){p.ColumnStyles.Add(new(SizeType.Percent,25));var card=new InspectionCard(kind,colors[(int)kind],ChooseInspection);cards[kind]=card;p.Controls.Add(card,(int)kind,0);}
        p.Controls.Add(scopeStats,0,1);p.SetColumnSpan(scopeStats,4);return p;
    }
    Control BuildPreview()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=5,ColumnCount=1};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,60));p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,40));p.RowStyles.Add(new(SizeType.AutoSize));var filters=AppTheme.Flow();var more=AppTheme.Flow();more.Visible=false;more.Controls.AddRange([valueFilter,reasonFilter]);var toggle=new LinkLabel{Text="更多筛选",AutoSize=true,Margin=new Padding(10,9,4,0)};toggle.LinkClicked+=(_,_)=>more.Visible=!more.Visible;
        filters.Controls.AddRange([AppTheme.Label("类型"),type,inspection,stateFilter,refFilter,toggle,AppTheme.Button("清空筛选",AppTheme.Muted,(_,_)=>{type.SelectedIndex=0;refFilter.Clear();valueFilter.Clear();reasonFilter.Clear();}),more]);p.Controls.Add(filters,0,0);p.Controls.Add(grid,0,1);p.Controls.Add(AppTheme.Label("本组位号 · 英文句点分隔，可直接粘贴到 Excel",9),0,2);p.Controls.Add(refs,0,3);var actions=AppTheme.Flow();actions.Controls.AddRange([copyOne,copyFour]);p.Controls.Add(actions,0,4);return p;
    }
    Control BuildIssues()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.AutoSize));p.Controls.Add(AppTheme.Label("检测结果以 Coverage 为准；这里只选择盲点原因或提示文件数据问题。"),0,0);p.Controls.Add(issues,0,1);p.Controls.Add(AppTheme.Button("选择盲点原因",AppTheme.Orange,(_,_)=>ChooseReason()),0,2);return p;
    }
    Control BuildRules()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.AutoSize));var scope=AppTheme.Flow();scope.Controls.AddRange([projectInfo]);p.Controls.Add(scope,0,0);
        var actions=AppTheme.Flow();actions.Controls.AddRange([AppTheme.Button("新增规则",AppTheme.Green,async(_,_)=>await EditRule(true)),AppTheme.Button("编辑规则",AppTheme.Blue,async(_,_)=>await EditRule(false)),AppTheme.Button("删除规则",AppTheme.Orange,async(_,_)=>await ArchiveRule(true)),AppTheme.Button("恢复规则",AppTheme.Muted,async(_,_)=>await ArchiveRule(false)),archived]);p.Controls.Add(actions,0,1);p.Controls.Add(rulesGrid,0,2);p.Controls.Add(AppTheme.Label("删除可恢复，原编号及来源保留。核心规则请在 V2规则总览查看。",9),0,3);return p;
    }
    Control BuildRuleOverview()
    {
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.Controls.Add(AppTheme.Label("当前执行只按 Coverage 的 X / √ / ∕；历史依据仅供追溯，不要求查看测试程序。",9),0,0);panel.Controls.Add(ruleList,0,1);return panel;
    }
    void AddPage(string name,Control control){control.Dock=DockStyle.Fill;content.Controls.Add(control);pages[name]=control;}
    void ShowPage(string name){foreach(var p in pages)p.Value.Visible=p.Key==name;pages[name].BringToFront();title.Text=name;foreach(var pair in nav){bool active=pair.Key==name;pair.Value.BackColor=active?AppTheme.Yellow:AppTheme.Navy;pair.Value.ForeColor=active?AppTheme.Ink:Color.White;}}
    sealed record InspectionChoice(InspectionKind Kind){public override string ToString()=>Kind.Name();}
    sealed record StatusChoice(TestState? State,string Label){public override string ToString()=>Label;}
    InspectionKind ActiveKind=>(inspection.SelectedItem as InspectionChoice)?.Kind??InspectionKind.Missing;
    FilterOptions CurrentFilter()=>new(type.Text,valueFilter.Text.Trim(),reasonFilter.Text.Trim(),refFilter.Text.Trim(),false,(stateFilter.SelectedItem as StatusChoice)?.State);
    void RefreshChoices()
    {
        refreshing=true;var oldType=type.Text;var oldKind=ActiveKind;int oldStatus=stateFilter.SelectedIndex;
        type.Items.Clear();type.Items.Add("全部");foreach(var t in cfg.KnownTypes)type.Items.Add(t);type.SelectedItem=type.Items.Contains(oldType)?oldType:"全部";
        inspection.Items.Clear();foreach(var k in Enum.GetValues<InspectionKind>())if(report==null||report.Analyses.ContainsKey(k))inspection.Items.Add(new InspectionChoice(k));
        inspection.SelectedItem=inspection.Items.Cast<InspectionChoice>().FirstOrDefault(c=>c.Kind==oldKind)??inspection.Items.Cast<InspectionChoice>().FirstOrDefault();
        stateFilter.Items.Clear();stateFilter.Items.AddRange(new object[]{new StatusChoice(TestState.Untestable,"Х 不可测"),new StatusChoice(TestState.Testable,"√ 可测"),new StatusChoice(TestState.NotApplicable,"∕ 不适用"),new StatusChoice(null,"全部状态")});stateFilter.SelectedIndex=oldStatus>=0?oldStatus:0;
        RefreshRules();refreshing=false;
    }
    void ActivateInspection(){if(report==null)return;analysis=report.Analyses.GetValueOrDefault(ActiveKind);RefreshResults();}
    void ChooseInspection(InspectionKind kind,TestState? state)
    {
        if(report==null||!report.Analyses.ContainsKey(kind))return;
        refreshing=true;inspection.SelectedItem=inspection.Items.Cast<InspectionChoice>().Single(c=>c.Kind==kind);if(state.HasValue)stateFilter.SelectedItem=stateFilter.Items.Cast<StatusChoice>().Single(s=>s.State==state);refreshing=false;ActivateInspection();
    }
    void RefreshSidebar()
    {
        if(analysis==null){sidebarStats.Text="导入报告后自动识别项目";return;}
        sidebarStats.Text=$"整板 · {ActiveKind.Name()}\nPCBA总点数：{analysis.Total}\n不可测点数：{analysis.Untestable}\n测试覆盖率：{cfg.FormatCoverage(analysis.Coverage)}\n值针率：{(needle.Text.Trim().Length==0?"未提供":needle.Text.Trim())}";
        projectInfo.Text="项目：自动识别 · "+(report?.ProjectProfile is {Length:>0} project?project:"未匹配专属项目，使用基础规则");
    }
    void RefreshRules()
    {
        rulesGrid.DataSource=cfg.Rules.Where(RuleManagementService.CanEdit).Where(r=>archived.Checked||!r.Archived).OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,检测项目=string.Join("/",r.Inspections.Select(k=>k.Name())),类型=string.Join("/",r.Types),后缀=r.Suffix??"不限",标准值=r.ExactValue??"不限",优先级=r.Priority,状态=r.Archived?"已删除":r.Enabled?"已启用":"停用",原因=r.Reason.Length==0?"仅参考":cfg.ReasonText(r.Reason),项目=r.Profile??"通用"}).ToList();ruleList.DataSource=cfg.Rules.OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,检测项目=string.Join("/",r.Inspections.Select(k=>k.Name())),阶段=r.Stage,优先级=r.Priority,当前执行=RulePresentation.CurrentExecution(r),项目=r.Profile??"通用",历史依据=RulePresentation.HistoricalBasis(r),历史来源=r.Source}).ToList();RefreshLog();RefreshReasons();
    }
    async Task RunBusy(Func<Task> action)
    {
        if(busy)return;busy=true;UseWaitCursor=true;foreach(Control c in Controls)c.Enabled=false;
        try{await action();}catch(Exception ex){status.Text="操作未完成："+ex.Message;MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;UseWaitCursor=false;foreach(Control c in Controls)c.Enabled=true;ShowGroup();}
    }
    async Task ReadAsync()=>await RunBusy(async()=>
    {
        if(!File.Exists(input.Text))throw new FileNotFoundException("请选择 Coverage 报告");SaveSession();string path=input.Text;var reader=new CoverageReader();status.Text="正在整理报告…";var snapshot=await Task.Run(()=>CoverageSnapshot.Read(path));var maps=await Task.Run(()=>reader.Locate(snapshot));if(maps.Count==0)throw new InvalidDataException("未找到 ICT → 缺件、标准值和位号，请检查报告表头");var map=maps[0];if(maps.Count>1){using var form=new ChoiceForm<FieldMap>("选择检测工作表",maps);if(form.ShowDialog(this)!=DialogResult.OK)return;map=form.Selected!;}
        report=await Task.Run(()=>{var loaded=reader.ReadReport(snapshot,map);ReportWorkflow.Analyze(loaded,cfg);return loaded;});LoadSession();RefreshChoices();ActivateInspection();
        if(analysis!=null){board.Text=analysis.BoardNumber;product.Text=analysis.ProductNumber;status.Text=$"已整理 {analysis.Total} 个元件 / {ActiveKind.Name()}盲点 {analysis.Untestable}；读取已保存内容。";}

    });
    void RefreshResults()
    {
        if(analysis==null||report==null)return;var f=CurrentFilter();var filtered=AnalysisEngine.Filter(analysis,f,cfg);groups=AnalysisEngine.Group(filtered);
        grid.DataSource=groups.Select((g,i)=>new{索引=i,状态=g.Items[0].State switch{TestState.Testable=>"√ 可测",TestState.NotApplicable=>"∕ 不适用",TestState.Unknown or TestState.Conflict=>"未知符号",_=>g.HasDataIssues?"数据待处理":g.Pending?"原因待确定":"Х 已归类"},元件类型=g.Items[0].GroupCategory,元件位置=ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace('\n',' '),盲点数量=g.Count,标准值=g.Value,盲点原因=g.Items[0].State==TestState.Untestable?cfg.ReasonText(g.ReasonCode):"—"}).ToList();
        if(grid.Columns.Contains("索引"))grid.Columns["索引"].Visible=false;if(grid.Columns.Contains("元件位置"))grid.Columns["元件位置"].FillWeight=240;
        foreach(var col in new[]{"状态","盲点数量"})if(grid.Columns.Contains(col)){grid.Columns[col].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;grid.Columns[col].Width=105;}if(grid.Columns.Contains("盲点数量"))grid.Columns["盲点数量"].HeaderText="数量";
        foreach(var pair in cards)pair.Value.Update(report.Analyses.TryGetValue(pair.Key,out var a)?InspectionStatistics.From(AnalysisEngine.BaseFilter(a,f)):null,pair.Key==ActiveKind);
        var scope=AnalysisEngine.BaseFilter(analysis,f);scopeStats.Text=$"统计范围：{type.Text} · {scope.Count}个元件；当前{ActiveKind.Name()}结果 {filtered.Count} 条（状态/原因筛选不改变统计分母）";
        nav["异常 / 待确认"].Text="异常 / 待确认"+(analysis.Pending>0?$"  {analysis.Pending}":"");
        issues.DataSource=scope.Where(c=>c.Pending).Select(c=>new{源行=c.SourceRow,位号=c.Reference,元件类型=c.Type,标准值=c.Value,建议原因=c.ReasonCode.Length==0?"请选择":cfg.ReasonText(c.ReasonCode),问题=string.Join("；",c.Issues.Where(i=>i.Blocking).Select(i=>i.Message))}).ToList();RefreshLog();RefreshSidebar();if(analysis.Issues.Count>0)status.Text=string.Join("；",analysis.Issues.Select(i=>i.Message));ShowGroup();
    }
    void RefreshLog()=>log.DataSource=report==null?null:ManualResults.Get(report,cfg).Select(c=>new{元件位号=c.Reference,元件类型=c.Type,检测项目=c.Inspection,标准值=c.Value,盲点原因=c.Reason}).ToList();
    BlindGroup? SelectedGroup()=>grid.CurrentRow?.Cells["索引"].Value is int i&&i>=0&&i<groups.Count?groups[i]:null;
    void ShowGroup(){var g=SelectedGroup();refs.Text=g==null?"":ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace("\n","\r\n");copyOne.Enabled=copyFour.Enabled=g!=null&&!g.HasDataIssues&&g.Items.All(c=>c.State==TestState.Untestable);}
    void CopyGroup(bool four){try{var g=SelectedGroup();if(g==null)return;ClipboardService.Copy(ClipboardService.GroupCells(g,cfg,(int)width.Value,four));status.Text=$"已复制 {g.Count} 个位号"+(four?"及数量、标准值、原因，粘贴到 Excel B 列即可。":"，粘贴到元件位置单元格即可。");}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
    async void ChooseReason()
    {
        var c=analysis?.Components.FirstOrDefault(c=>c.SourceRow==Convert.ToInt32(issues.CurrentRow?.Cells["源行"].Value??-1));if(c==null||analysis==null)return;
        if(c.SourceState!=TestState.Untestable){MessageBox.Show(this,"请检查源报告的空白或未知检测符号；软件不修改检测结果。");return;}
        using var form=new CauseForm(c,cfg,ActiveKind);if(form.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            if(form.NewReasonText!=null){cfg=ReasonManagementService.Upsert(cfg,form.ReasonCode,form.NewReasonText,form.Reviewer,form.Justification);RuleManagementService.Save(cfg,RulePath);RefreshReasons();}
            AnalysisEngine.Edit(analysis,c,c.Reference,form.ComponentType,form.StandardValue,c.SourceState,form.ReasonCode,form.Note,form.Justification,cfg,form.Reviewer);SaveSession();RefreshResults();
            if(form.SaveAsRule)
            {
                var rule=new ClassificationRule{RuleId=RuleManagementService.NewId(cfg),Stage="原因分类",Status="人工校准",Source="用户人工选择："+Path.GetFileName(analysis.SourcePath)+" 第"+c.SourceRow+"行",Inspections=[ActiveKind],Types=[c.Type],Reference=c.Reference,ExactValue=c.RawValue,DisplayValue=c.Value,Reason=c.ReasonCode,Priority=Math.Max(0,c.MatchedRules.Select(m=>m.Priority).DefaultIfEmpty(81).Min()-1),Supersedes=c.MatchedRules.Select(m=>m.RuleId).ToArray(),Profile=ProjectIdentification.Identity(analysis) is {Length:>0} id?id:null};rule.Scope=rule.Profile==null?"report_profile":"project";
                using var editor=new RuleEditorForm(cfg,rule);if(editor.ShowDialog(this)==DialogResult.OK)await ApplyRules(RuleManagementService.Update(cfg,editor.Rule,editor.Reviewer,editor.Justification,true,editor.NewReasonText));
            }
        }catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    async Task ExportAsync()=>await RunBusy(async()=>
    {
        if(report==null||analysis==null)throw new InvalidOperationException("请先导入报告");var selection=ReportWorkflow.SelectExport(report,ActiveKind,CurrentFilter(),cfg);bool draft=false;
        if(selection.Result.DataIssues>0){if(MessageBox.Show(this,$"当前范围有 {selection.Result.DataIssues} 条位号或文件数据问题。是否生成待审核盲点表？未知原因直接填写“待确定”。","文件数据问题",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){ShowPage("异常 / 待确认");return;}draft=true;}
        using var d=new SaveFileDialog{Filter="盲点表|*.xlsx",FileName=Path.GetFileNameWithoutExtension(analysis.SourcePath)+"-"+ActiveKind.Name()+(draft?"-盲点表-待审核.xlsx":"-盲点表.xlsx")};if(d.ShowDialog(this)!=DialogResult.OK)return;
        await WriteBlindPointAsync(d.FileName,new(board.Text,product.Text,needle.Text,note.Text,(int)width.Value,draft));status.Text="已导出盲点表："+d.FileName;using var result=new ExportResultForm(d.FileName);result.ShowDialog(this);
    });
    internal async Task WriteBlindPointAsync(string target,ExportOptions options)
    {
        if(report==null)throw new InvalidOperationException("请先导入报告");var selection=ReportWorkflow.SelectExport(report,ActiveKind,CurrentFilter(),cfg);
        var actual=options with{BoardTotal=selection.BoardTotal,ScopeTotal=selection.ScopeTotal,ScopeCoverage=selection.ScopeCoverage,ScopeLabel=selection.ScopeLabel};await Task.Run(()=>new ExcelExporter(cfg).ExportFixed(selection.Result,target,actual));
    }
    string? SelectedRule()=>rulesGrid.CurrentRow?.Cells["编号"].Value as string;
    async Task EditRule(bool create)
    {
        var original=create?null:cfg.Rules.FirstOrDefault(r=>r.RuleId==SelectedRule());if(!create&&original==null)return;using var form=new RuleEditorForm(cfg,original,ActiveKind);if(form.ShowDialog(this)!=DialogResult.OK)return;try{await ApplyRules(RuleManagementService.Update(cfg,form.Rule,form.Reviewer,form.Justification,create,form.NewReasonText));}catch(Exception ex){MessageBox.Show(this,ex.Message,"规则未保存");}
    }
    async Task ArchiveRule(bool archive)
    {
        string? id=SelectedRule();if(id==null)return;var rule=cfg.Rules.Single(r=>r.RuleId==id);if(archive==rule.Archived)return;using var form=new ChangeReasonForm(archive?"删除规则（可恢复）":"恢复规则");if(form.ShowDialog(this)!=DialogResult.OK)return;try{await ApplyRules(RuleManagementService.Archive(cfg,id,archive,form.Reviewer,form.Justification));}catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    async Task ApplyRules(RuleConfig next)
    {
        SaveSession();RuleManagementService.Save(next,RulePath);cfg=next;
        if(report!=null){var saved=JsonSerializer.Deserialize<CoverageReport>(JsonSerializer.Serialize(report,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;
            await RunBusy(async()=>{await Task.Run(()=>{foreach(var a in report.Analyses.Values){a.Edits.Clear();a.Corrections.Clear();}ReportWorkflow.Analyze(report,cfg);ReportWorkflow.Restore(report,saved,cfg);});});}
        RefreshChoices();ActivateInspection();SaveSession();status.Text="规则已保存并应用，已选择的人工原因结果保留。";
    }
    string SessionPath()=>Path.Combine(userDir,(report?.SourceFingerprint??analysis?.SourceFingerprint??throw new InvalidOperationException("尚未读取报告"))+"-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions)+report?.ProjectProfile)))[..12]+".v4.session.json");
    void SaveSession()
    {
        if(report==null||!report.Analyses.Values.Any(a=>a.Edits.Count>0||a.Corrections.Count>0))return;
        File.WriteAllText(SessionPath(),JsonSerializer.Serialize(report,RuleConfig.JsonOptions));
    }
    void LoadSession()
    {
        if(report==null)return;string exact=SessionPath();string? path=File.Exists(exact)?exact:Directory.GetFiles(userDir,report.SourceFingerprint+"-*.session.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(path==null)return;
        if(MessageBox.Show(this,"找到此报告的最终人工原因记录，是否恢复？","恢复记录",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        try
        {
            string json=File.ReadAllText(path);using var doc=JsonDocument.Parse(json);CoverageReport saved;
            if(doc.RootElement.TryGetProperty("Analyses",out _)||doc.RootElement.TryGetProperty("analyses",out _))saved=JsonSerializer.Deserialize<CoverageReport>(json,RuleConfig.JsonOptions)!;
            else{var old=JsonSerializer.Deserialize<Analysis>(json,RuleConfig.JsonOptions)!;saved=new(){SourceFingerprint=old.SourceFingerprint,Analyses={{InspectionKind.Missing,old}}};}
            ReportWorkflow.Restore(report,saved,cfg);
        }catch(Exception ex){MessageBox.Show(this,"未能恢复旧记录，当前报告数据保持有效："+ex.Message);}
    }
}
