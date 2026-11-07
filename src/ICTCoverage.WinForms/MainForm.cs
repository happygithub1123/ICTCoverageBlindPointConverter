using ICTCoverage.Core;
using System.Text.Json;
using System.Security.Cryptography;
namespace ICTCoverage.WinForms;

public sealed partial class MainForm : Form
{
    readonly string userDir;string RulePath=>Path.Combine(userDir,"rules-v6.json");readonly string defaults=Path.Combine(AppContext.BaseDirectory,"config","rules.json");
    RuleConfig cfg;Analysis? analysis;CoverageReport? report;InspectionKind? selectedKind;bool busy,refreshing,onlyCausePending;
    readonly TextBox input=new(){Dock=DockStyle.Fill,ReadOnly=true},refFilter=new(){Width=125,PlaceholderText="位号"},valueFilter=new(){Width=115,PlaceholderText="标准值"},reasonFilter=new(){Width=135,PlaceholderText="原因关键词"},board=new(){Width=140},product=new(){Width=160},needle=new(){Width=90},note=new(){Width=160};
    readonly ComboBox type=new(){Width=120,DropDownStyle=ComboBoxStyle.DropDownList},stateFilter=new(){Width=120,DropDownStyle=ComboBoxStyle.DropDownList},batchReason=new(){Width=280,DropDownWidth=550,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly NumericUpDown width=new(){Minimum=20,Maximum=150,Value=48,Width=65};
    readonly ReportTable grid=new();
    readonly DataGridView copyGrid=AppTheme.Grid(),issues=AppTheme.Grid(),rulesGrid=AppTheme.Grid(),ruleList=AppTheme.Grid(),log=AppTheme.Grid();
    readonly TextBox refs=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",12),BackColor=Color.White};
    readonly Label title=AppTheme.Label("分类筛选",18,true),status=AppTheme.Label("导入Coverage，默认查看全部元件与四项结果。",10),sidebarStats=new(){Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",13.5f,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},scopeStats=AppTheme.Label("导入后显示统计",10),projectInfo=AppTheme.Label("项目自动识别",10);
    readonly Dictionary<string,Control> pages=[];readonly Dictionary<string,Button> nav=[];readonly Dictionary<InspectionKind,Button> inspectionButtons=[];Button allInspection=null!;
    readonly Panel content=new(){Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(8)};Control statsPanel=null!,filterPanel=null!;
    readonly CheckBox archived=new(){Text="显示已删除规则",AutoSize=true},selectAllIssues=new(){Text="全选当前筛选",AutoSize=true,Margin=new Padding(5,12,8,0)};
    readonly List<OverviewCard> overview=[];readonly Button copyOne,copyFour;List<BlindGroup> groups=[];readonly List<(CauseTarget Target,Component Item)> issueTargets=[];
    readonly Panel copyTools=new(){Dock=DockStyle.Fill,Visible=false};TableLayoutPanel copyLayout=null!;bool copyExpanded;
    sealed record StatusChoice(TestState? State,string Text){public override string ToString()=>Text;}
    sealed record ReasonChoice(string Code,string Text){public override string ToString()=>Text;}
    public MainForm(string? dataDirectory=null)
    {
        userDir=dataDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ICTCoverageBlindPointConverter");Directory.CreateDirectory(userDir);Directory.CreateDirectory(SessionDirectory);
        if(!File.Exists(RulePath)){string old=Path.Combine(userDir,"rules-v5.json");if(!File.Exists(old))old=Path.Combine(userDir,"rules-v4.json");if(!File.Exists(old))old=Path.Combine(userDir,"rules-v3.json");if(!File.Exists(old))old=Path.Combine(userDir,"rules-v2.json");try{RuleManagementService.Save(RuleManagementService.Upgrade(RuleConfig.Load(File.Exists(old)?old:defaults),RuleConfig.Load(defaults)),RulePath);}catch{File.Copy(defaults,RulePath);}}
        try{cfg=RuleConfig.Load(RulePath);}catch{File.Copy(RulePath,RulePath+".invalid-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true);cfg=RuleConfig.Load(defaults);RuleManagementService.Save(cfg,RulePath);}
        var bundled=RuleConfig.Load(defaults);if(Version.TryParse(cfg.Version,out var oldVersion)&&Version.TryParse(bundled.Version,out var newVersion)&&oldVersion<newVersion){File.Copy(RulePath,RulePath+".before-v0.5.0",true);cfg=RuleManagementService.Upgrade(cfg,bundled);RuleManagementService.Save(cfg,RulePath);}
        Text="ICT盲点表工具 · WHAPPY v0.5.4";Font=new Font("Microsoft YaHei UI",11);Width=1440;Height=960;MinimumSize=new Size(1120,760);AutoScaleMode=AutoScaleMode.Dpi;BackColor=AppTheme.Background;StartPosition=FormStartPosition.CenterScreen;
        using(var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.ico"))if(stream!=null)Icon=new Icon(stream);
        copyOne=AppTheme.Button("复制本组位号",AppTheme.Blue,(_,_)=>CopyGroup(false));copyFour=AppTheme.Button("复制本组四列",AppTheme.Green,(_,_)=>CopyGroup(true));
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowCount=1;root.RowStyles.Add(new(SizeType.Percent,100));Controls.Add(root);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};body.RowStyles.Add(new(SizeType.Percent,100));body.ColumnStyles.Add(new(SizeType.Absolute,270));body.ColumnStyles.Add(new(SizeType.Percent,100));root.Controls.Add(body,0,0);body.Controls.Add(BuildNav(),0,0);
        var work=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(12,4,12,4)};work.ColumnStyles.Add(new(SizeType.Percent,100));for(int i=0;i<4;i++)work.RowStyles.Add(new(SizeType.AutoSize));work.RowStyles.Add(new(SizeType.Percent,100));work.RowStyles.Add(new(SizeType.AutoSize));body.Controls.Add(work,1,0);
        title.Margin=new Padding(5,2,5,2);statsPanel=BuildStats();filterPanel=BuildFilters();work.Controls.Add(title,0,0);work.Controls.Add(BuildImport(),0,1);work.Controls.Add(statsPanel,0,2);work.Controls.Add(filterPanel,0,3);work.Controls.Add(content,0,4);work.Controls.Add(status,0,5);
        AddPage("分类筛选",grid);AddPage("复制",BuildCopy());AddPage("异常待确认",BuildIssues());AddPage("盲点设置",BuildReasons());AddPage("类型分类设置",BuildTypes());AddPage("规则设置",BuildRules());AddPage("规则总览",BuildRuleOverview());AddPage("人工修改日志",log);AddPage("使用说明",BuildHelp());
        ConfigureMainGrid();ConfigureIssueGrid();RefreshChoices();ShowPage("分类筛选");copyGrid.SelectionChanged+=(_,_)=>ShowGroup();type.SelectedIndexChanged+=(_,_)=>{if(!refreshing)RefreshResults();};stateFilter.SelectedIndexChanged+=(_,_)=>{if(!refreshing){onlyCausePending=false;RefreshResults();}};foreach(var f in new[]{refFilter,valueFilter,reasonFilter})f.TextChanged+=(_,_)=>{if(!refreshing)ScheduleFilter();};width.ValueChanged+=(_,_)=>ShowGroup();archived.CheckedChanged+=(_,_)=>RefreshRules();needle.TextChanged+=(_,_)=>RefreshSidebar();
        grid.ReasonDoubleClick+=(_,_)=>ShowPage("异常待确认");issues.CurrentCellDirtyStateChanged+=(_,_)=>{if(issues.IsCurrentCellDirty)issues.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        AllowDrop=true;DragEnter+=(_,e)=>{if(e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};DragDrop+=async(_,e)=>{if(!busy&&e.Data?.GetData(DataFormats.FileDrop) is string[] p&&p.Length>0){input.Text=p[0];await ReadAsync();}};FormClosing+=HandleClosing;
    }
    static Button SideKindButton(string text,Color color,Action click){var b=AppTheme.Button(text,color,(_,_)=>click());b.MinimumSize=new Size(0,30);b.AutoSize=false;b.Dock=DockStyle.Fill;b.Margin=new Padding(3);b.Padding=new Padding(6,2,6,2);b.Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold);return b;}
    Control BuildImport()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1};p.ColumnStyles.Add(new(SizeType.Percent,100));var row=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4};row.ColumnStyles.Add(new(SizeType.Percent,100));for(int i=0;i<3;i++)row.ColumnStyles.Add(new(SizeType.AutoSize));input.Margin=new Padding(4,12,4,4);row.Controls.Add(input,0,0);row.Controls.Add(AppTheme.Button("导入报告",AppTheme.Blue,async(_,_)=>{using var d=new OpenFileDialog{Filter="Coverage|*.xls;*.xlsx"};if(d.ShowDialog(this)==DialogResult.OK){input.Text=d.FileName;await ReadAsync();}}),1,0);var refresh=AppTheme.Button("重新读取",AppTheme.Muted,async(_,_)=>await ReadAsync());refresh.MinimumSize=new Size(90,38);row.Controls.Add(refresh,2,0);row.Controls.Add(AppTheme.Button("导出盲点表",AppTheme.Green,async(_,_)=>await ExportAsync()),3,0);p.Controls.Add(row,0,0);
        var settings=AppTheme.Flow();settings.Visible=false;settings.Controls.AddRange([AppTheme.Label("光板编号"),board,AppTheme.Label("成品编号"),product,AppTheme.Label("值针率"),needle,AppTheme.Label("备注"),note,AppTheme.Label("换行宽度"),width]);var toggle=new LinkLabel{Text="输出设置 ▾",AutoSize=true,Margin=new Padding(5,0,5,3)};toggle.LinkClicked+=(_,_)=>settings.Visible=!settings.Visible;p.Controls.Add(toggle,0,1);p.Controls.Add(settings,0,2);return p;
    }
    Control BuildStats()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=5,RowCount=2};p.RowStyles.Add(new(SizeType.Absolute,82));p.RowStyles.Add(new(SizeType.AutoSize));string[] labels=["全部元件","不可测 Х","可测 √","不适用 ∕","原因待选择"];Color[] colors=[AppTheme.Ink,AppTheme.Blue,AppTheme.Green,AppTheme.Muted,AppTheme.Orange];for(int i=0;i<5;i++){p.ColumnStyles.Add(new(SizeType.Percent,20));int index=i;var card=new OverviewCard(labels[i],colors[i],()=>ChooseOverview(index));overview.Add(card);p.Controls.Add(card,i,0);}p.Controls.Add(scopeStats,0,1);p.SetColumnSpan(scopeStats,5);return p;
    }
    Control BuildFilters()
    {
        refFilter.Width=110;valueFilter.Width=110;reasonFilter.Width=120;var p=AppTheme.Flow();var clear=AppTheme.Button("清空筛选",AppTheme.Muted,(_,_)=>ResetFilters());clear.MinimumSize=new Size(90,38);p.Controls.AddRange([AppTheme.Label("类型"),type,stateFilter,refFilter,valueFilter,reasonFilter,clear]);return p;
    }
    void ResetFilters(){refreshing=true;type.SelectedItem="全部";stateFilter.SelectedIndex=0;refFilter.Clear();valueFilter.Clear();reasonFilter.Clear();onlyCausePending=false;refreshing=false;RefreshResults();}
    void AddPage(string name,Control control){control.Dock=DockStyle.Fill;content.Controls.Add(control);pages[name]=control;}
    void ShowPage(string name){bool pending=filterDelay.Enabled;filterDelay.Stop();currentPage=name;if(pending)RefreshResults();content.SuspendLayout();foreach(var p in pages)p.Value.Visible=p.Key==name;pages[name].BringToFront();content.ResumeLayout(true);title.Text=PageLabel(name);statsPanel.Visible=name=="分类筛选";filterPanel.Visible=name is "分类筛选" or "复制" or "异常待确认";foreach(var pair in nav){bool active=pair.Key==name;pair.Value.BackColor=active?AppTheme.Blue:AppTheme.Navy;pair.Value.ForeColor=Color.White;}RefreshVisiblePage();}
    FilterOptions CurrentFilter()=>new(type.Text,valueFilter.Text.Trim(),reasonFilter.Text.Trim(),refFilter.Text.Trim(),false,(stateFilter.SelectedItem as StatusChoice)?.State,onlyCausePending);
    void ChooseInspection(InspectionKind? kind){if(kind.HasValue&&report!=null&&!report.Analyses.ContainsKey(kind.Value))return;selectedKind=kind;onlyCausePending=false;ActivateInspection();}
    void ActivateInspection(){analysis=report?.Analyses.GetValueOrDefault(selectedKind??InspectionKind.Missing)??report?.Analyses.Values.FirstOrDefault();RefreshResults();}
    void ChooseOverview(int index){refreshing=true;onlyCausePending=index==4;TestState? state=index switch{1 or 4=>TestState.Untestable,2=>TestState.Testable,3=>TestState.NotApplicable,_=>null};stateFilter.SelectedItem=stateFilter.Items.Cast<StatusChoice>().Single(c=>c.State==state);refreshing=false;ShowPage("分类筛选");RefreshResults();}
    void RefreshChoices()
    {
        refreshing=true;string oldType=type.Text;var oldState=(stateFilter.SelectedItem as StatusChoice)?.State;type.Items.Clear();type.Items.AddRange(new object[]{"全部","待确认"});foreach(var t in cfg.KnownTypes)type.Items.Add(t);type.SelectedItem=type.Items.Contains(oldType)?oldType:"全部";
        stateFilter.Items.Clear();stateFilter.Items.AddRange(new object[]{new StatusChoice(null,"全部状态"),new StatusChoice(TestState.Untestable,"Х 不可测"),new StatusChoice(TestState.Testable,"√ 可测"),new StatusChoice(TestState.NotApplicable,"∕ 不适用"),new StatusChoice(TestState.Unknown,"未知符号")});stateFilter.SelectedItem=stateFilter.Items.Cast<StatusChoice>().First(c=>c.State==oldState);RefreshBatchReasons();dirtyPages.UnionWith(new[]{"规则设置","规则总览","盲点设置","类型分类设置","人工修改日志"});refreshing=false;RefreshVisiblePage();
    }
    void RefreshResults()
    {
        filterDelay.Stop();if(report==null||analysis==null)return;var f=CurrentFilter();var scope=CurrentView().Where(r=>(f.Type=="全部"||f.Type=="待确认"&&r.Pending||r.Type==f.Type)&&r.Value.Contains(f.Value,StringComparison.OrdinalIgnoreCase)&&(r.Reference.Contains(f.Reference,StringComparison.OrdinalIgnoreCase)||r.RawReference.Contains(f.Reference,StringComparison.OrdinalIgnoreCase))).ToList();
        var rows=scope.Where(r=>(!f.Status.HasValue||r.State==f.Status)&&(!f.OnlyPendingCause||r.PendingCause)&&r.Reason.Contains(f.Reason,StringComparison.OrdinalIgnoreCase)).ToList();DisplayMainRows(rows);
        int[] values=[scope.Count,scope.Count(r=>r.State==TestState.Untestable),scope.Count(r=>r.State==TestState.Testable),scope.Count(r=>r.State==TestState.NotApplicable),scope.Count(r=>r.PendingCause)];var state=(stateFilter.SelectedItem as StatusChoice)?.State;int active=onlyCausePending?4:state switch{TestState.Untestable=>1,TestState.Testable=>2,TestState.NotApplicable=>3,null=>0,_=>-1};for(int i=0;i<5;i++)overview[i].Update(values[i],i==active);
        scopeStats.Text=$"{(selectedKind?.Name()??"全部检测项目")} · 源记录 {scope.Count} · 当前筛选 {rows.Count}";RefreshSidebar();InvalidateDetailPages();
    }
    void RefreshSidebar()
    {
        if(analysis==null)return;var a=selectedKind.HasValue?report!.Analyses.GetValueOrDefault(selectedKind.Value):report!.Analyses.GetValueOrDefault(InspectionKind.Missing);sidebarStats.Text=$"PCBA总点数：{analysis.Total}\n不可测点数：{(a==null?"未提供":a.Untestable)}\n覆盖率：{(a==null?"未提供":cfg.FormatCoverage(a.Coverage))}\n值针率：{(needle.Text.Trim().Length==0?"未提供":needle.Text.Trim())}";
        projectInfo.Text="自动项目："+(report!.ProjectProfile.Length>0?report.ProjectProfile:"基础规则");allInspection.BackColor=selectedKind==null?AppTheme.Blue:AppTheme.Muted;foreach(var pair in inspectionButtons){bool provided=report.Analyses.TryGetValue(pair.Key,out var item);pair.Value.Enabled=provided;pair.Value.Text=pair.Key.Name()+" "+(provided?InspectionStatistics.From(item!.Components).Percentage:"未提供");pair.Value.FlatAppearance.BorderSize=selectedKind==pair.Key?2:0;pair.Value.FlatAppearance.BorderColor=Color.White;}
    }
    void RefreshLog()=>log.DataSource=report==null?null:ManualResults.Get(report,cfg).Select(c=>new{元件位号=c.Reference,元件类型=c.Type,检测项目=c.Inspection,标准值=c.Value,盲点原因=c.Reason}).ToList();
    async Task RunBusy(Func<Task> action){if(busy)return;busy=true;UseWaitCursor=true;foreach(Control c in Controls)c.Enabled=false;try{await action();}catch(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;UseWaitCursor=false;foreach(Control c in Controls)c.Enabled=true;}}
    async Task ReadAsync()=>await RunBusy(async()=>
    {
        string path=input.Text;if(!File.Exists(path))throw new FileNotFoundException("请选择Coverage报告");
        filterDelay.Stop();status.Text="正在保存上次修改…";await SaveSessionAsync();
        var reader=new CoverageReader();status.Text="正在读取Coverage文件…";
        var snapshot=await Task.Run(()=>CoverageSnapshot.Read(path));
        status.Text="正在自动识别ICT检测列…";var maps=await Task.Run(()=>reader.Locate(snapshot));
        if(maps.Count==0)throw new InvalidDataException("没有找到ICT检测表头、位号和标准值");
        var map=maps[0];if(maps.Count>1){using var choose=new ChoiceForm<FieldMap>("选择工作表",maps);if(choose.ShowDialog(this)!=DialogResult.OK)return;map=choose.Selected!;}
        status.Text="正在读取元件并匹配盲点原因…";
        var incoming=await Task.Run(()=>{var r=reader.ReadReport(snapshot,map);ReportWorkflow.Analyze(r,cfg);return r;});
        await LoadSessionAsync(incoming);
        report=incoming;selectedKind=null;analysis=report.Analyses.GetValueOrDefault(InspectionKind.Missing)??report.Analyses.Values.First();
        CollapseCopyTools();board.Text=analysis.BoardNumber;product.Text=analysis.ProductNumber;
        // Reset filters once; hidden pages are refreshed only when opened.
        currentPage="分类筛选";ResetFilters();ShowPage("分类筛选");
        status.Text=$"已读取{analysis.Total}条源记录；默认全部类型、全部检测项目、全部状态。";
    });
    async Task ExportAsync()=>await RunBusy(async()=>{if(report==null)throw new InvalidOperationException("请先导入报告");var selected=ReportView.FullBlindPoint(report,cfg);bool draft=selected.Result.DataIssues>0;if(draft&&MessageBox.Show(this,"报告存在文件数据问题，是否生成待审核版本？","数据问题",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;using var d=new SaveFileDialog{Filter="盲点表|*.xlsx",FileName=Path.GetFileNameWithoutExtension(report.SourcePath)+"-盲点表"+(draft?"-待审核":"")+".xlsx"};if(d.ShowDialog(this)!=DialogResult.OK)return;await WriteBlindPointAsync(d.FileName,new(board.Text,product.Text,needle.Text,note.Text,(int)width.Value,draft));using var result=new ExportResultForm(d.FileName);result.ShowDialog(this);});
    internal async Task WriteBlindPointAsync(string target,ExportOptions options){var selected=ReportView.FullBlindPoint(report??throw new InvalidOperationException("请导入报告"),cfg);await Task.Run(()=>new ExcelExporter(cfg).ExportFixed(selected.Result,target,options with{BoardTotal=selected.BoardTotal,ScopeTotal=selected.ScopeTotal,ScopeCoverage=selected.ScopeCoverage,ScopeLabel=null}));}
    string SessionPath()=>SessionPath(report??throw new InvalidOperationException("未读取报告"),cfg);
    string SessionPath(CoverageReport current,RuleConfig config)=>Path.Combine(SessionDirectory,current.SourceFingerprint+"-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config,RuleConfig.JsonOptions)+current.ProjectProfile)))[..12]+".v6.session.json");
    async Task ApplyRules(RuleConfig next)=>await RunBusy(async()=>
    {
        status.Text="正在保存并重新匹配规则…";await SaveSessionAsync();var saved=report;
        var updated=await Task.Run(()=>
        {
            CoverageReport? result=null;
            if(saved!=null)
            {
                result=saved.Copy();
                foreach(var a in result.Analyses.Values){a.Edits.Clear();a.Corrections.Clear();}
                ReportWorkflow.Analyze(result,next);ReportWorkflow.Restore(result,saved,next);
            }
            RuleManagementService.Save(next,RulePath);
            if(result!=null&&result.Analyses.Values.Any(a=>a.Edits.Count>0||a.Corrections.Count>0))BatchReasonService.Save(result,SessionPath(result,next));
            return result;
        });
        cfg=next;report=updated;RefreshChoices();ActivateInspection();status.Text="设置已保存并同步。";
    });
}
