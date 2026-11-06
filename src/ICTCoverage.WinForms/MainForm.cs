using ICTCoverage.Core;
using System.Text.Json;
using System.Security.Cryptography;
namespace ICTCoverage.WinForms;

public sealed class MainForm : Form
{
    readonly string userDir;
    string RulePath=>Path.Combine(userDir,"rules-v3.json");
    readonly string defaults=Path.Combine(AppContext.BaseDirectory,"config","rules.json");
    RuleConfig cfg; Analysis? analysis; bool busy,refreshing;
    readonly TextBox input=new(){Dock=DockStyle.Fill,ReadOnly=true,PlaceholderText="选择或拖入 Coverage 报告（.xls / .xlsx）"},refFilter=new(){Width=185,PlaceholderText="搜索位号"},valueFilter=new(){Width=145,PlaceholderText="标准值"},reasonFilter=new(){Width=175,PlaceholderText="盲点原因"},board=new(){Width=150},product=new(){Width=150},needle=new(){Width=85},note=new(){Width=155,PlaceholderText="留空沿用模板"};
    readonly ComboBox type=new(){Width=120,DropDownStyle=ComboBoxStyle.DropDownList},profile=new(){Width=260,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly NumericUpDown width=new(){Minimum=20,Maximum=150,Value=48,Width=65};
    readonly DataGridView grid=AppTheme.Grid(),issues=AppTheme.Grid(),rulesGrid=AppTheme.Grid(),ruleList=AppTheme.Grid(),log=AppTheme.Grid();
    readonly TextBox refs=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",11),BorderStyle=BorderStyle.None,BackColor=Color.White};
    readonly Label status=AppTheme.Label("仅整理 ICT 缺件列打 X 的元件；√ 和 ∕ 不纳入盲点表。",9),title=AppTheme.Label("分类筛选 / 复制",19,true);
    readonly Panel content=new(){Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12)};
    readonly Dictionary<string,Control> pages=[]; readonly Dictionary<string,Button> nav=[];
    readonly CheckBox archived=new(){Text="显示已删除规则",AutoSize=true,Margin=new Padding(12,15,0,0)};
    readonly Label[] counts=Enumerable.Range(0,5).Select(_=>new Label{Text="—",AutoSize=true,Font=new Font("Microsoft YaHei UI",20,FontStyle.Bold),Margin=new Padding(10,2,0,0)}).ToArray();
    readonly Button copyOne,copyFour;
    List<BlindGroup> groups=[];
    public MainForm(string? dataDirectory=null)
    {
        userDir=dataDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ICTCoverageBlindPointConverter");Directory.CreateDirectory(userDir);
        if(!File.Exists(RulePath)){
            string old=Path.Combine(userDir,"rules-v2.json");
            try{var c=RuleConfig.Load(File.Exists(old)?old:defaults);c.Version="2.1.0";c.NotApplicableSymbols=["∕","/","／"];RuleManagementService.Save(c,RulePath);}catch{File.Copy(defaults,RulePath);}
        }
        try{cfg=RuleConfig.Load(RulePath);}catch{File.Copy(RulePath,RulePath+".invalid-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true);cfg=RuleConfig.Load(defaults);RuleManagementService.Save(cfg,RulePath);}
        Text="ICT 盲点表工具 · WHAPPY";Font=new Font("Microsoft YaHei UI",10);BackColor=AppTheme.Background;ForeColor=AppTheme.Ink;Width=1440;Height=960;MinimumSize=new Size(1120,760);AutoScaleMode=AutoScaleMode.Dpi;StartPosition=FormStartPosition.CenterScreen;
        using(var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.ico"))if(stream!=null)Icon=new Icon(stream);
        copyOne=AppTheme.Button("复制本组位号",AppTheme.Blue,(_,_)=>CopyGroup(false));copyFour=AppTheme.Button("复制本组四列",AppTheme.Green,(_,_)=>CopyGroup(true));
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};root.ColumnStyles.Add(new(SizeType.Absolute,220));root.ColumnStyles.Add(new(SizeType.Percent,100));Controls.Add(root);root.Controls.Add(BuildNav(),0,0);
        var work=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(18,8,18,8)};for(int i=0;i<3;i++)work.RowStyles.Add(new(SizeType.AutoSize));work.RowStyles.Add(new(SizeType.Percent,100));work.RowStyles.Add(new(SizeType.AutoSize));root.Controls.Add(work,1,0);
        work.Controls.Add(title,0,0);work.Controls.Add(BuildImport(),0,1);work.Controls.Add(BuildStats(),0,2);work.Controls.Add(content,0,3);work.Controls.Add(status,0,4);
        AddPage("分类筛选 / 复制",BuildPreview());AddPage("异常 / 待确认",BuildIssues());AddPage("规则设置",BuildRules());AddPage("V2规则总览",ruleList);AddPage("人工修改日志",log);RefreshChoices();ShowPage("分类筛选 / 复制");
        grid.SelectionChanged+=(_,_)=>ShowGroup();type.SelectedIndexChanged+=(_,_)=>RefreshResults();foreach(var f in new[]{refFilter,valueFilter,reasonFilter})f.TextChanged+=(_,_)=>RefreshResults();width.ValueChanged+=(_,_)=>ShowGroup();archived.CheckedChanged+=(_,_)=>RefreshRules();profile.SelectedIndexChanged+=async(_,_)=>{if(!refreshing&&analysis!=null)await ReadAsync();};
        AllowDrop=true;DragEnter+=(_,e)=>{if(e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};DragDrop+=async(_,e)=>{if(!busy&&e.Data?.GetData(DataFormats.FileDrop) is string[] p&&p.Length>0){input.Text=p[0];await ReadAsync();}};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;return;}SaveSession();};
    }
    Control BuildNav()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=AppTheme.Navy,ColumnCount=1,RowCount=8,Padding=new Padding(12,18,12,12)};p.RowStyles.Add(new(SizeType.Absolute,175));for(int i=0;i<5;i++)p.RowStyles.Add(new(SizeType.Absolute,58));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.Absolute,100));
        var brand=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};using(var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.png"))if(stream!=null)brand.Controls.Add(new PictureBox{Image=new Bitmap(stream),Width=68,Height=68,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(8,0,0,8)});
        brand.Controls.Add(new Label{Text="ICT 盲点表工具",AutoSize=true,ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold)});brand.Controls.Add(new Label{Text="Coverage → Blind Points",AutoSize=true,ForeColor=Color.FromArgb(174,190,216),Margin=new Padding(4,8,0,0)});p.Controls.Add(brand,0,0);
        string[] names=["分类筛选 / 复制","异常 / 待确认","规则设置","V2规则总览","人工修改日志"];
        for(int i=0;i<names.Length;i++){string name=names[i];var b=AppTheme.Button(name,AppTheme.Navy,(_,_)=>ShowPage(name));b.Dock=DockStyle.Fill;b.TextAlign=ContentAlignment.MiddleLeft;b.Font=new Font(Font,i<2?FontStyle.Bold:FontStyle.Regular);nav[name]=b;p.Controls.Add(b,0,i+1);}
        p.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Designed by WHAPPY\n设计人员：王开心\nWANG KAIXIN  ·  v0.3.0",ForeColor=Color.FromArgb(208,218,235),Font=new Font("Microsoft YaHei UI",9),TextAlign=ContentAlignment.BottomLeft},0,7);return p;
    }
    Control BuildImport()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=3};var row=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4};row.ColumnStyles.Add(new(SizeType.Percent,100));for(int i=0;i<3;i++)row.ColumnStyles.Add(new(SizeType.AutoSize));input.Margin=new Padding(4,12,8,4);row.Controls.Add(input,0,0);
        row.Controls.Add(AppTheme.Button("导入报告",AppTheme.Blue,async(_,_)=>{using var d=new OpenFileDialog{Filter="Coverage报告|*.xls;*.xlsx"};if(d.ShowDialog(this)==DialogResult.OK){input.Text=d.FileName;await ReadAsync();}}),1,0);row.Controls.Add(AppTheme.Button("读取分析",AppTheme.Blue,async(_,_)=>await ReadAsync()),2,0);row.Controls.Add(AppTheme.Button("导出盲点表",AppTheme.Green,async(_,_)=>await ExportAsync()),3,0);p.Controls.Add(row,0,0);
        var settings=AppTheme.Flow();settings.Visible=false;settings.Controls.AddRange([AppTheme.Label("光板编号"),board,AppTheme.Label("成品编号"),product,AppTheme.Label("值针率"),needle,AppTheme.Label("备注"),note,AppTheme.Label("换行宽度"),width]);var toggle=new LinkLabel{Text="输出设置 ▾",AutoSize=true,LinkColor=AppTheme.Muted,Margin=new Padding(5,3,0,6)};toggle.LinkClicked+=(_,_)=>{settings.Visible=!settings.Visible;toggle.Text=settings.Visible?"收起输出设置 ▴":"输出设置 ▾";};p.Controls.Add(toggle,0,1);p.Controls.Add(settings,0,2);return p;
    }
    Control BuildStats()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=5,Padding=new Padding(0,4,0,10)};string[] labels=["全部元件","不可测 X","可测 √","不适用 ∕","原因待选择"];Color[] colors=[AppTheme.Ink,AppTheme.Blue,AppTheme.Green,AppTheme.Muted,AppTheme.Orange];
        for(int i=0;i<5;i++){p.ColumnStyles.Add(new(SizeType.Percent,20));var card=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,BackColor=Color.White,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(4),Padding=new Padding(4,5,4,5)};var label=AppTheme.Label(labels[i],9);label.ForeColor=AppTheme.Muted;label.Margin=new Padding(10,2,0,0);counts[i].ForeColor=colors[i];card.Controls.Add(label);card.Controls.Add(counts[i]);p.Controls.Add(card,i,0);}return p;
    }
    Control BuildPreview()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=5,ColumnCount=1};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,60));p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,40));p.RowStyles.Add(new(SizeType.AutoSize));var filters=AppTheme.Flow();var more=AppTheme.Flow();more.Visible=false;more.Controls.AddRange([valueFilter,reasonFilter]);var toggle=new LinkLabel{Text="更多筛选",AutoSize=true,Margin=new Padding(10,9,4,0)};toggle.LinkClicked+=(_,_)=>more.Visible=!more.Visible;
        filters.Controls.AddRange([AppTheme.Label("元件类型"),type,refFilter,toggle,AppTheme.Button("清空筛选",AppTheme.Muted,(_,_)=>{type.SelectedIndex=0;refFilter.Clear();valueFilter.Clear();reasonFilter.Clear();}),more]);p.Controls.Add(filters,0,0);p.Controls.Add(grid,0,1);p.Controls.Add(AppTheme.Label("本组位号 · 英文句点分隔，可直接粘贴到 Excel",9),0,2);p.Controls.Add(refs,0,3);var actions=AppTheme.Flow();actions.Controls.AddRange([copyOne,copyFour]);p.Controls.Add(actions,0,4);return p;
    }
    Control BuildIssues()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.AutoSize));p.Controls.Add(AppTheme.Label("检测结果以 Coverage 为准；这里只选择盲点原因或提示文件数据问题。"),0,0);p.Controls.Add(issues,0,1);p.Controls.Add(AppTheme.Button("选择盲点原因",AppTheme.Orange,(_,_)=>ChooseReason()),0,2);return p;
    }
    Control BuildRules()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.AutoSize));var scope=AppTheme.Flow();scope.Controls.AddRange([AppTheme.Label("项目规则"),profile,AppTheme.Label("自动识别或选择通用模式，关闭项目特例",9)]);p.Controls.Add(scope,0,0);
        var actions=AppTheme.Flow();actions.Controls.AddRange([AppTheme.Button("新增规则",AppTheme.Green,async(_,_)=>await EditRule(true)),AppTheme.Button("编辑规则",AppTheme.Blue,async(_,_)=>await EditRule(false)),AppTheme.Button("删除规则",AppTheme.Orange,async(_,_)=>await ArchiveRule(true)),AppTheme.Button("恢复规则",AppTheme.Muted,async(_,_)=>await ArchiveRule(false)),archived]);p.Controls.Add(actions,0,1);p.Controls.Add(rulesGrid,0,2);p.Controls.Add(AppTheme.Label("删除可恢复，原编号及来源保留。核心规则请在 V2规则总览查看。",9),0,3);return p;
    }
    void AddPage(string name,Control control){control.Dock=DockStyle.Fill;content.Controls.Add(control);pages[name]=control;}
    void ShowPage(string name){foreach(var p in pages)p.Value.Visible=p.Key==name;pages[name].BringToFront();title.Text=name;foreach(var pair in nav){bool active=pair.Key==name;pair.Value.BackColor=active?AppTheme.Yellow:AppTheme.Navy;pair.Value.ForeColor=active?AppTheme.Ink:Color.White;}}
    void RefreshChoices()
    {
        refreshing=true;string oldType=type.Text,oldProfile=profile.Text;type.Items.Clear();type.Items.AddRange(new object[]{"全部","电阻","电容","电感","二极管","三极管","IC","连接器","晶振","其他","待确认"});type.SelectedItem=type.Items.Contains(oldType)?oldType:"全部";profile.Items.Clear();profile.Items.Add("自动识别项目");profile.Items.Add("通用（关闭项目特例）");foreach(var project in cfg.ProfileValueOverrides.Keys.Union(cfg.Rules.Where(r=>r.Profile!=null).Select(r=>r.Profile!)))profile.Items.Add(project);profile.SelectedItem=profile.Items.Contains(oldProfile)?oldProfile:"自动识别项目";RefreshRules();refreshing=false;
    }
    void RefreshRules()
    {
        rulesGrid.DataSource=cfg.Rules.Where(RuleManagementService.CanEdit).Where(r=>archived.Checked||!r.Archived).OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,类型=string.Join("/",r.Types),后缀=r.Suffix??"不限",标准值=r.ExactValue??"不限",优先级=r.Priority,状态=r.Archived?"已删除":r.Enabled?"已启用":"停用",原因=r.Reason.Length==0?"仅参考":cfg.ReasonText(r.Reason),项目=r.Profile??"通用"}).ToList();ruleList.DataSource=cfg.Rules.OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,阶段=r.Stage,优先级=r.Priority,状态=r.Archived?"已删除":r.Enabled?"启用":"停用",依据=r.Status,项目=r.Profile??"通用",来源=r.Source}).ToList();RefreshLog();
    }
    async Task RunBusy(Func<Task> action)
    {
        if(busy)return;busy=true;UseWaitCursor=true;foreach(Control c in Controls)c.Enabled=false;
        try{await action();}catch(Exception ex){status.Text="操作未完成："+ex.Message;MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;UseWaitCursor=false;foreach(Control c in Controls)c.Enabled=true;ShowGroup();}
    }
    async Task ReadAsync()=>await RunBusy(async()=>
    {
        if(!File.Exists(input.Text))throw new FileNotFoundException("请选择 Coverage 报告");SaveSession();string path=input.Text;var reader=new CoverageReader();status.Text="正在整理报告…";var maps=await Task.Run(()=>reader.Locate(path));if(maps.Count==0)throw new InvalidDataException("未找到 ICT → 缺件、标准值和位号，请检查报告表头");var map=maps[0];if(maps.Count>1){using var form=new ChoiceForm<FieldMap>("选择检测工作表",maps);if(form.ShowDialog(this)!=DialogResult.OK)return;map=form.Selected!;}
        string selected=profile.SelectedIndex>1?profile.Text:profile.SelectedIndex==0?cfg.ProfileValueOverrides.Keys.FirstOrDefault(p=>Path.GetFileName(path).StartsWith(p,StringComparison.OrdinalIgnoreCase))??"":"";analysis=await Task.Run(()=>{var a=reader.Read(path,map);new AnalysisEngine(cfg).Analyze(a,selected);return a;});LoadSession();board.Text=analysis.BoardNumber;product.Text=analysis.ProductNumber;RefreshResults();if(analysis.Issues.Count==0)status.Text=$"已整理 {analysis.Total} 个元件 / {analysis.Untestable} 个盲点，仅根据 X / √ / ∕。";
    });
    void RefreshResults()
    {
        if(analysis==null)return;var filtered=AnalysisEngine.Filter(analysis,new(type.Text,valueFilter.Text.Trim(),reasonFilter.Text.Trim(),refFilter.Text.Trim()),cfg);groups=AnalysisEngine.Group(filtered);grid.DataSource=groups.Select((g,i)=>new{索引=i,状态=g.Pending?"待选择原因":"已归类",元件位置=ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace('\n',' '),盲点数量=g.Count,标准值=g.Value,盲点原因=cfg.ReasonText(g.ReasonCode)}).ToList();if(grid.Columns.Contains("索引"))grid.Columns["索引"].Visible=false;if(grid.Columns.Contains("元件位置"))grid.Columns["元件位置"].FillWeight=240;foreach(var col in new[]{"状态","盲点数量"})if(grid.Columns.Contains(col)){grid.Columns[col].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;grid.Columns[col].Width=105;}
        counts[0].Text=analysis.Total.ToString();counts[1].Text=analysis.Untestable.ToString();counts[2].Text=analysis.Testable.ToString();counts[3].Text=analysis.Components.Count(c=>c.State==TestState.NotApplicable).ToString();counts[4].Text=analysis.Pending.ToString();nav["异常 / 待确认"].Text="异常 / 待确认"+(analysis.Pending>0?$"  {analysis.Pending}":"");
        issues.DataSource=analysis.Components.Where(c=>c.Pending).Select(c=>new{源行=c.SourceRow,位号=c.Reference,元件类型=c.Type,标准值=c.Value,建议原因=c.ReasonCode.Length==0?"请选择":cfg.ReasonText(c.ReasonCode),问题=string.Join("；",c.Issues.Where(i=>i.Blocking).Select(i=>i.Message))}).ToList();RefreshLog();if(analysis.Issues.Count>0)status.Text=string.Join("；",analysis.Issues.Select(i=>i.Message));ShowGroup();
    }
    sealed record LogRow(string 时间,string 类型,string 对象,string 修改人,string 理由,string 修改前,string 修改后);
    void RefreshLog()=>log.DataSource=cfg.RuleChanges.Select(e=>new LogRow(e.Time.ToString("yyyy-MM-dd HH:mm:ss"),"规则 · "+e.Action,e.RuleId,e.Reviewer,e.Reason,e.Before,e.After)).Concat((analysis?.Edits??[]).Select(e=>new LogRow(e.Time.ToString("yyyy-MM-dd HH:mm:ss"),"原因选择","源行 "+e.SourceRow,e.Reviewer,e.Reason,e.Before,e.After))).OrderByDescending(e=>e.时间).ToList();
    BlindGroup? SelectedGroup()=>grid.CurrentRow?.Cells["索引"].Value is int i&&i>=0&&i<groups.Count?groups[i]:null;
    void ShowGroup(){var g=SelectedGroup();refs.Text=g==null?"":ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace("\n","\r\n");copyOne.Enabled=copyFour.Enabled=g!=null&&!g.Pending&&g.Items.All(c=>c.State==TestState.Untestable);}
    void CopyGroup(bool four){try{var g=SelectedGroup();if(g==null)return;ClipboardService.Copy(ClipboardService.GroupCells(g,cfg,(int)width.Value,four));status.Text=$"已复制 {g.Count} 个位号"+(four?"及数量、标准值、原因，粘贴到 Excel B 列即可。":"，粘贴到元件位置单元格即可。");}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
    void ChooseReason()
    {
        var c=analysis?.Components.FirstOrDefault(c=>c.SourceRow==Convert.ToInt32(issues.CurrentRow?.Cells["源行"].Value??-1));if(c==null||analysis==null)return;if(c.SourceState!=TestState.Untestable){MessageBox.Show(this,"请检查源报告的空白或未知检测符号；软件不修改检测结果。");return;}using var form=new CauseForm(c,cfg);if(form.ShowDialog(this)!=DialogResult.OK)return;
        try{AnalysisEngine.Edit(analysis,c,c.Reference,form.ComponentType,form.StandardValue,c.SourceState,form.ReasonCode,form.Note,form.Justification,cfg,form.Reviewer);SaveSession();RefreshResults();}catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    async Task ExportAsync()=>await RunBusy(async()=>
    {
        if(analysis==null)throw new InvalidOperationException("请先导入报告");bool draft=false;if(analysis.Pending>0){if(MessageBox.Show(this,$"还有 {analysis.Pending} 条原因或数据待处理。是否生成待审核盲点表？","原因待选择",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){ShowPage("异常 / 待确认");return;}draft=true;}
        using var d=new SaveFileDialog{Filter="盲点表|*.xlsx",FileName=Path.GetFileNameWithoutExtension(analysis.SourcePath)+(draft?"-盲点表-待审核.xlsx":"-盲点表.xlsx")};if(d.ShowDialog(this)!=DialogResult.OK)return;await WriteBlindPointAsync(d.FileName,new(board.Text,product.Text,needle.Text,note.Text,(int)width.Value,draft));status.Text="已导出盲点表："+d.FileName;using var result=new ExportResultForm(d.FileName);result.ShowDialog(this);
    });
    internal async Task WriteBlindPointAsync(string target,ExportOptions options){if(analysis==null)throw new InvalidOperationException("请先导入报告");await Task.Run(()=>new ExcelExporter(cfg).ExportFixed(analysis,target,options));}
    string? SelectedRule()=>rulesGrid.CurrentRow?.Cells["编号"].Value as string;
    async Task EditRule(bool create)
    {
        var original=create?null:cfg.Rules.FirstOrDefault(r=>r.RuleId==SelectedRule());if(!create&&original==null)return;using var form=new RuleEditorForm(cfg,original);if(form.ShowDialog(this)!=DialogResult.OK)return;try{await ApplyRules(RuleManagementService.Update(cfg,form.Rule,form.Reviewer,form.Justification,create,form.NewReasonText));}catch(Exception ex){MessageBox.Show(this,ex.Message,"规则未保存");}
    }
    async Task ArchiveRule(bool archive)
    {
        string? id=SelectedRule();if(id==null)return;var rule=cfg.Rules.Single(r=>r.RuleId==id);if(archive==rule.Archived)return;using var form=new ChangeReasonForm(archive?"删除规则（可恢复）":"恢复规则");if(form.ShowDialog(this)!=DialogResult.OK)return;try{await ApplyRules(RuleManagementService.Archive(cfg,id,archive,form.Reviewer,form.Justification));}catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    async Task ApplyRules(RuleConfig next){SaveSession();RuleManagementService.Save(next,RulePath);cfg=next;RefreshChoices();if(analysis!=null){analysis=null;if(File.Exists(input.Text))await ReadAsync();}status.Text="规则已保存并应用，原人工选择保留在历史会话中。";}
    string SessionPath()=>Path.Combine(userDir,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(analysis!.SourcePath)))+"-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions)+analysis.Profile)))[..12]+".session.json");
    void SaveSession(){if(analysis==null||analysis.Edits.Count==0)return;analysis.BoardNumber=board.Text;analysis.ProductNumber=product.Text;File.WriteAllText(SessionPath(),JsonSerializer.Serialize(analysis,RuleConfig.JsonOptions));}
    void LoadSession(){if(analysis==null)return;string p=SessionPath();if(File.Exists(p)&&MessageBox.Show(this,"找到此报告的原因选择记录，是否恢复？","恢复记录",MessageBoxButtons.YesNo)==DialogResult.Yes){var saved=JsonSerializer.Deserialize<Analysis>(File.ReadAllText(p),RuleConfig.JsonOptions);if(saved!=null){saved.SourcePath=analysis.SourcePath;analysis=saved;}}}
}
