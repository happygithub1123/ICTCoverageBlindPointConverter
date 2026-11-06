using ICTCoverage.Core;
using System.Text.Json;
using System.Security.Cryptography;

namespace ICTCoverage.WinForms;

public sealed class MainForm : Form
{
    readonly string defaultRules=Path.Combine(AppContext.BaseDirectory,"config","rules.json");
    readonly string userDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ICTCoverageBlindPointConverter");
    string RulePath=>Path.Combine(userDir,"rules-v2.json");
    RuleConfig cfg=null!; Analysis? analysis;
    List<Component> filtered=[]; List<BlindGroup> groups=[];
    readonly TextBox input=new(){Width=620},template=new(){Width=620},board=new(){Width=180},product=new(){Width=180},needle=new(){Width=85},note=new(){Width=150,PlaceholderText="留空沿用模板备注"};
    readonly ComboBox type=new(){Width=110,DropDownStyle=ComboBoxStyle.DropDownList},profile=new(){Width=270,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly TextBox valueFilter=new(){Width=125},reasonFilter=new(){Width=180},refFilter=new(){Width=130};
    readonly NumericUpDown width=new(){Minimum=20,Maximum=150,Value=48,Width=62};
    readonly CheckBox only=new(){Text="仅不可测",Checked=true,AutoSize=true};
    readonly Label stats=new(){AutoSize=true,Padding=new Padding(8)},status=new(){Dock=DockStyle.Bottom,Height=28,Text="请选择 Coverage 报告",Padding=new Padding(8,4,0,0)};
    readonly DataGridView grid=NewGrid(),detail=NewGrid(),issues=NewGrid(),log=NewGrid(),ruleList=NewGrid();
    readonly TextBox refs=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",11)};
    readonly TextBox ruleEditor=new(){Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10)};
    readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    readonly ProgressBar progress=new(){Dock=DockStyle.Bottom,Height=5,Visible=false,Style=ProgressBarStyle.Marquee};
    readonly Button copyOne=new(){Text="复制本组位号",AutoSize=true},copyFour=new(){Text="复制本组四列",AutoSize=true};
    bool busy;
    static DataGridView NewGrid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,AutoGenerateColumns=true};
    static FlowLayoutPanel Flow()=>new(){Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(4)};
    static Label Caption(string text)=>new(){Text=text,AutoSize=true,Margin=new Padding(8,8,3,0)};
    static Button Button(string text,EventHandler action){var b=new Button{Text=text,AutoSize=true,Margin=new Padding(4)};b.Click+=action;return b;}
    public MainForm(string? dataDirectory=null)
    {
        if(dataDirectory!=null)userDir=dataDirectory;
        Text="ICT Coverage V2 · 盲点表生成与分类筛选";Width=1420;Height=960;MinimumSize=new Size(1100,780);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9);
        Directory.CreateDirectory(userDir);if(!File.Exists(RulePath))File.Copy(defaultRules,RulePath);
        try{cfg=RuleConfig.Load(RulePath);}catch{cfg=RuleConfig.Load(defaultRules);MessageBox.Show("用户规则无效，本次使用默认规则，请在规则设置中检查。文件未覆盖。");}
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
        for(int i=0;i<5;i++)root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.Percent,100));
        var files=Flow(); files.Controls.AddRange([Caption("Coverage"),input,Button("选择报告",(_,_)=>Pick(input,"Excel|*.xls;*.xlsx")),Button("读取分析",async(_,_)=>await ReadAsync())]);root.Controls.Add(files,0,0);
        var temp=Flow();temp.Controls.AddRange([Caption("输出模板"),template,Button("选择模板",(_,_)=>Pick(template,"Excel模板|*.xlsx")),Button("生成盲点表",async(_,_)=>await ExportAsync(false)),Button("核对报告",async(_,_)=>await ExportAsync(true))]);root.Controls.Add(temp,0,1);
        var meta=Flow();meta.Controls.AddRange([Caption("光板编号"),board,Caption("成品编号"),product,Caption("值针率"),needle,Caption("默认备注"),note,Caption("项目规则"),profile]);root.Controls.Add(meta,0,2);
        var filters=Flow();filters.Controls.AddRange([Caption("类型"),type,Caption("标准值"),valueFilter,Caption("原因"),reasonFilter,Caption("位号"),refFilter,only,Caption("换行宽度"),width,Button("清空筛选",(_,_)=>{type.SelectedIndex=0;valueFilter.Clear();reasonFilter.Clear();refFilter.Clear();only.Checked=true;})]);root.Controls.Add(filters,0,3);
        root.Controls.Add(stats,0,4);root.Controls.Add(tabs,0,5);Controls.Add(root);Controls.Add(status);Controls.Add(progress);
        var preview=new TabPage("分类筛选 / 复制");var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=260,Size=new Size(1200,600)};
        split.Panel1.Controls.Add(grid);var lower=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=2};lower.ColumnStyles.Add(new(SizeType.Percent,42));lower.ColumnStyles.Add(new(SizeType.Percent,58));lower.RowStyles.Add(new(SizeType.AutoSize));lower.RowStyles.Add(new(SizeType.Percent,100));
        var buttons=Flow();buttons.Controls.AddRange([copyOne,copyFour,Button("复制筛选内全部位号",(_,_)=>CopyAll()),Button("修改选中明细",(_,_)=>EditSelected()),Button("复制原始明细",(_,_)=>CopyRaw())]);lower.Controls.Add(buttons,0,0);lower.SetColumnSpan(buttons,2);lower.Controls.Add(refs,0,1);lower.Controls.Add(detail,1,1);split.Panel2.Controls.Add(lower);preview.Controls.Add(split);tabs.TabPages.Add(preview);
        var auditTab=new TabPage("异常 / 待确认");auditTab.Controls.Add(issues);tabs.TabPages.Add(auditTab);
        var auditButtons=Flow();auditButtons.Dock=DockStyle.Top;auditButtons.Controls.AddRange([Button("修改选中异常",(_,_)=>EditIssue()),Button("复制异常原始明细",(_,_)=>CopyIssue())]);auditTab.Controls.Add(auditButtons);
        var rulesTab=new TabPage("规则设置");rulesTab.Controls.Add(ruleEditor);var rb=Flow();rb.Dock=DockStyle.Top;rb.Controls.AddRange([Button("保存并应用",(_,_)=>SaveRules()),Button("导入 JSON",(_,_)=>ImportRules()),Button("导出 JSON",(_,_)=>ExportRules()),Button("加载默认到编辑器",(_,_)=>ruleEditor.Text=File.ReadAllText(defaultRules)),Caption("V2：按显式优先级匹配并保留候选；10%/30%不计算，程序原因需审核。")]);rulesTab.Controls.Add(rb);tabs.TabPages.Add(rulesTab);
        var ruleListTab=new TabPage("V2规则总览");ruleListTab.Controls.Add(ruleList);tabs.TabPages.Add(ruleListTab);
        var logsTab=new TabPage("人工修改日志");logsTab.Controls.Add(log);tabs.TabPages.Add(logsTab);
        ruleEditor.Text=JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions);RefreshChoices();
        var sampleDir=Path.Combine(AppContext.BaseDirectory,"samples");if(Directory.Exists(sampleDir)){input.Text=Directory.GetFiles(sampleDir,"*.xls").FirstOrDefault()??"";template.Text=Directory.GetFiles(sampleDir,"*.xlsx").FirstOrDefault()??"";}
        grid.SelectionChanged+=(_,_)=>ShowGroup();copyOne.Click+=(_,_)=>CopyGroup(false);copyFour.Click+=(_,_)=>CopyGroup(true);
        type.SelectedIndexChanged+=(_,_)=>RefreshResults();only.CheckedChanged+=(_,_)=>RefreshResults();width.ValueChanged+=(_,_)=>ShowGroup();
        valueFilter.TextChanged+=(_,_)=>RefreshResults();reasonFilter.TextChanged+=(_,_)=>RefreshResults();refFilter.TextChanged+=(_,_)=>RefreshResults();
        AllowDrop=true;DragEnter+=(_,e)=>{if(e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};DragDrop+=(_,e)=>{if(!busy&&e.Data?.GetData(DataFormats.FileDrop) is string[] paths&&paths.Length>0)input.Text=paths[0];};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;return;}if(analysis!=null)SaveSession();};
        Shown+=(_,_)=>split.SplitterDistance=Math.Max(160,split.Height*40/100);
    }
    void RefreshChoices()
    {
        type.Items.Clear();type.Items.AddRange(new object[]{"全部","电阻","电容","电感","二极管","三极管","IC","连接器","晶振","其他","待确认"});type.SelectedIndex=0;
        ruleList.DataSource=cfg.Rules.OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,阶段=r.Stage,优先级=r.Priority,启用=r.Enabled,可靠性=r.Status,作用域=r.Profile??r.Scope,原因=cfg.ReasonText(r.Reason),来源=r.Source}).ToList();
        profile.Items.Clear();profile.Items.Add("自动识别项目");profile.Items.Add("通用（关闭项目特例）");foreach(var p in cfg.ProfileValueOverrides.Keys)profile.Items.Add(p);profile.SelectedIndex=0;
    }
    static void Pick(TextBox box,string filter){using var d=new OpenFileDialog{Filter=filter};if(d.ShowDialog()==DialogResult.OK)box.Text=d.FileName;}
    async Task RunBusy(Func<Task> action)
    {
        if(busy)return;busy=true;progress.Visible=true;foreach(Control c in Controls)if(c!=progress&&c!=status)c.Enabled=false;
        try{await action();}catch(Exception ex){status.Text="操作未完成";MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;progress.Visible=false;foreach(Control c in Controls)c.Enabled=true;}
    }
    async Task ReadAsync()=>await RunBusy(async()=>
    {
        string path=input.Text.Trim();if(!File.Exists(path))throw new FileNotFoundException("请选择有效的Coverage文件");
        if(analysis!=null)SaveSession();
        status.Text="正在读取并识别字段…";var reader=new CoverageReader();var maps=await Task.Run(()=>reader.Locate(path));
        if(maps.Count==0)throw new InvalidDataException("未识别到 ICT → 缺件、标准值（或RISK）和位号列。请检查工作表表头；未导入任何记录。");
        FieldMap map=maps[0];if(maps.Count>1){using var chooser=new ChoiceForm<FieldMap>("选择Coverage工作表和检测列",maps);if(chooser.ShowDialog(this)!=DialogResult.OK)return;map=chooser.Selected!;}
        string selectedProfile=profile.SelectedIndex>1?profile.Text:"";
        if(profile.SelectedIndex==0)selectedProfile=cfg.ProfileValueOverrides.Keys.FirstOrDefault(p=>Path.GetFileName(path).StartsWith(p,StringComparison.OrdinalIgnoreCase))??"";
        analysis=await Task.Run(()=>{var a=reader.Read(path,map);new AnalysisEngine(cfg).Analyze(a,selectedProfile);return a;});
        LoadSession();board.Text=analysis.BoardNumber;product.Text=analysis.ProductNumber;RefreshResults();status.Text=$"已读取：{map}；规则配置：{(analysis.Profile.Length==0?"通用":analysis.Profile)}";
    });
    void RefreshResults()
    {
        if(analysis==null)return;
        filtered=AnalysisEngine.Filter(analysis,new(type.Text,valueFilter.Text.Trim(),reasonFilter.Text.Trim(),refFilter.Text.Trim(),only.Checked),cfg);
        groups=AnalysisEngine.Group(filtered);
        grid.DataSource=groups.Select((g,i)=>new{索引=i,状态=g.Pending?"待确认":"可复制",元件位置=ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace('\n',' '),盲点数量=g.Count,标准值=g.Value,盲点原因=cfg.ReasonText(g.ReasonCode)}).ToList();
        if(grid.Columns.Contains("索引"))grid.Columns["索引"].Visible=false;
        if(grid.Columns.Contains("元件位置"))grid.Columns["元件位置"].FillWeight=270;
        foreach(var name in new[]{"状态","盲点数量"})if(grid.Columns.Contains(name)){grid.Columns[name].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;grid.Columns[name].Width=85;}
        stats.Text=$"源记录 {analysis.Total}　源可测 {analysis.Testable}　源不可测 {analysis.SourceUntestable}　输出盲点 {analysis.Untestable}　待确认 {analysis.Pending}　│ 当前筛选 {filtered.Count} 条（不可测 {filtered.Count(c=>c.State==TestState.Untestable)}） / {groups.Count} 组 / 待确认 {filtered.Count(c=>c.Pending)}";
        if(analysis.Issues.Count>0)stats.Text+="　核对："+string.Join("；",analysis.Issues.Select(i=>i.Message));
        issues.DataSource=analysis.Components.Where(c=>c.Issues.Count>0).Select(c=>new{源行=c.SourceRow,原始位号=c.RawReference,规范位号=c.Reference,类型=c.Type,源状态=c.SourceState.ToString(),输出状态=c.State.ToString(),已人工确认=c.ManuallyReviewed,候选规则=string.Join(" / ",c.MatchedRules.Select(r=>r.RuleId)),待确认=c.Pending,问题=string.Join("；",c.Issues.Select(x=>x.Message))}).ToList();
        log.DataSource=analysis.Edits.Select(e=>new{时间=e.Time.ToString("yyyy-MM-dd HH:mm:ss"),源行=e.SourceRow,修改人=e.Reviewer,规则版本=e.RuleVersion,理由=e.Reason,修改前=e.Before,修改后=e.After}).ToList();ShowGroup();
    }
    BlindGroup? SelectedGroup()=>grid.CurrentRow?.Cells["索引"].Value is int i&&i>=0&&i<groups.Count?groups[i]:null;
    void ShowGroup()
    {
        var g=SelectedGroup();refs.Text=g==null?"":ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace("\n","\r\n");
        copyOne.Enabled=copyFour.Enabled=g!=null&&!g.Pending&&g.Items.All(c=>c.State==TestState.Untestable);
        detail.DataSource=g?.Items.Select(c=>new{源行=c.SourceRow,原始位号=c.RawReference,规范位号=c.Reference,标准值=c.Value,原因=cfg.ReasonText(c.ReasonCode),规则=c.RuleId,源状态=c.SourceState.ToString(),输出状态=c.State.ToString(),候选规则=string.Join(" / ",c.MatchedRules.Select(r=>r.RuleId)),问题=string.Join("；",c.Issues.Select(x=>x.Message))}).ToList();
    }
    void CopyGroup(bool four){try{var g=SelectedGroup();if(g==null)return;ClipboardService.Copy(ClipboardService.GroupCells(g,cfg,(int)width.Value,four));status.Text=$"已复制 {g.Count} 个位号"+(four?"及数量、标准值、原因；在Excel从B列普通粘贴":"；在Excel目标单元格普通粘贴");}catch(Exception ex){MessageBox.Show(ex.Message);}}
    void CopyAll(){if(filtered.Count==0)return;ClipboardService.Copy([ReferenceFormatter.Format(filtered.Select(c=>c.Reference),(int)width.Value)]);status.Text=$"已复制当前筛选的 {filtered.Count} 条位号，仅供整理核对";}
    Component? SelectedComponent(DataGridView view)=>analysis?.Components.FirstOrDefault(c=>c.SourceRow==Convert.ToInt32(view.CurrentRow?.Cells["源行"].Value??-1));
    void CopyRaw(){var c=SelectedComponent(detail);if(c!=null)ClipboardService.Copy([c.SourceRow.ToString(),c.RawReference,c.RawValue,c.StatusSymbol,string.Join("；",c.Issues.Select(x=>x.Message))]);}
    void CopyIssue(){var c=SelectedComponent(issues);if(c!=null)ClipboardService.Copy([c.SourceRow.ToString(),c.RawReference,c.RawValue,c.StatusSymbol,string.Join("；",c.Issues.Select(x=>x.Message))]);}
    void EditSelected()=>EditComponent(SelectedComponent(detail));
    void EditIssue()=>EditComponent(SelectedComponent(issues));
    void EditComponent(Component? c)
    {
        if(c==null||analysis==null)return;using var form=new EditForm(c,cfg);if(form.ShowDialog(this)!=DialogResult.OK)return;
        try{AnalysisEngine.Edit(analysis,c,form.Reference,form.ComponentType,form.StandardValue,form.State,form.ReasonCode,form.Note,form.Justification,cfg,form.Reviewer,form.ProgramEvidence);SaveSession();RefreshResults();}catch(Exception ex){MessageBox.Show(ex.Message);}
    }
    async Task ExportAsync(bool audit)=>await RunBusy(async()=>
    {
        if(analysis==null)throw new InvalidOperationException("请先读取分析");
        bool draft=false;if(!audit&&analysis.Pending>0){if(MessageBox.Show(this,$"还有 {analysis.Pending} 条待确认记录。是否生成“待审核”文件？待审核原因仍是建议；源统计和输出决定分别列入核对报告。","待审核导出",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;draft=true;}
        if(!audit&&!File.Exists(template.Text))throw new FileNotFoundException("请选择输出模板");
        using var d=new SaveFileDialog{Filter="Excel|*.xlsx",FileName=Path.GetFileNameWithoutExtension(analysis.SourcePath)+(audit?"-核对报告":draft?"-盲点表-待审核":"-盲点表")+".xlsx"};if(d.ShowDialog(this)!=DialogResult.OK)return;
        if(Path.GetFullPath(d.FileName)==Path.GetFullPath(analysis.SourcePath)||File.Exists(template.Text)&&Path.GetFullPath(d.FileName)==Path.GetFullPath(template.Text))throw new InvalidOperationException("不能覆盖源文件或模板");
        var options=new ExportOptions(board.Text,product.Text,needle.Text,note.Text,(int)width.Value,draft);string tp=template.Text;var exporter=new ExcelExporter(cfg);status.Text="正在生成并回读核对…";
        await Task.Run(()=>{if(audit)exporter.ExportAudit(analysis,d.FileName,File.Exists(tp)?tp:null);else{exporter.Export(analysis,tp,d.FileName,options);exporter.ExportAudit(analysis,Path.Combine(Path.GetDirectoryName(d.FileName)!,Path.GetFileNameWithoutExtension(d.FileName)+"-核对报告.xlsx"),tp);}});
        status.Text="已生成："+d.FileName;MessageBox.Show(this,"文件已生成。"+(audit?"":"已同时生成核对报告并通过位号回读校验。"),"完成");
    });
    void SaveRules()
    {
        string temp=Path.Combine(userDir,"rules.validation.json");
        try{File.WriteAllText(temp,ruleEditor.Text);var next=RuleConfig.Load(temp);
            if(analysis!=null&&MessageBox.Show(this,"应用规则需要重新读取报告。当前人工修订会保存到历史会话，新的规则分析不自动沿用。","应用规则",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            if(analysis!=null)SaveSession();File.Copy(temp,RulePath,true);cfg=next;analysis=null;grid.DataSource=null;detail.DataSource=null;issues.DataSource=null;refs.Clear();stats.Text="规则已应用，请重新读取分析";RefreshChoices();status.Text="规则已保存；按V2优先级匹配；矛盾候选进入审核，请用核对报告检查全部规则及来源。";
        }catch(Exception ex){MessageBox.Show(ex.Message,"规则无效");}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    void ImportRules(){using var d=new OpenFileDialog{Filter="规则JSON|*.json"};if(d.ShowDialog(this)==DialogResult.OK){try{RuleConfig.Load(d.FileName);ruleEditor.Text=File.ReadAllText(d.FileName);}catch(Exception ex){MessageBox.Show(ex.Message);}}}
    void ExportRules(){using var d=new SaveFileDialog{Filter="规则JSON|*.json",FileName="rules.json"};if(d.ShowDialog(this)==DialogResult.OK)File.WriteAllText(d.FileName,ruleEditor.Text);}
    string SessionPath()=>Path.Combine(userDir,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(analysis!.SourcePath)))+"-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg,RuleConfig.JsonOptions)+analysis.Profile)))[..12]+".session.json");
    void SaveSession(){if(analysis==null||analysis.Edits.Count==0)return;analysis.BoardNumber=board.Text;analysis.ProductNumber=product.Text;File.WriteAllText(SessionPath(),JsonSerializer.Serialize(analysis,RuleConfig.JsonOptions));}
    void LoadSession(){if(analysis==null)return;string path=SessionPath();if(File.Exists(path)&&MessageBox.Show(this,"找到同一源文件和规则配置的人工修订，是否恢复？","恢复修订",MessageBoxButtons.YesNo)==DialogResult.Yes){var saved=JsonSerializer.Deserialize<Analysis>(File.ReadAllText(path),RuleConfig.JsonOptions);if(saved!=null){saved.SourcePath=analysis.SourcePath;analysis=saved;}}}
}

public sealed class ChoiceForm<T> : Form
{
    readonly ListBox list=new(){Dock=DockStyle.Fill};public T? Selected=>list.SelectedItem is T x?x:default;
    public ChoiceForm(string title,List<T> choices){Text=title;Width=850;Height=260;StartPosition=FormStartPosition.CenterParent;list.Items.AddRange(choices.Cast<object>().ToArray());list.SelectedIndex=0;Controls.Add(list);var ok=new Button{Text="使用选中项",Dock=DockStyle.Bottom,Height=40,DialogResult=DialogResult.OK};Controls.Add(ok);AcceptButton=ok;}
}

public sealed class EditForm : Form
{
    readonly TextBox reference=new(),value=new(),note=new(),why=new(),reviewer=new(){Text=Environment.UserName},evidence=new();readonly ComboBox type=new(){DropDownStyle=ComboBoxStyle.DropDownList},state=new(){DropDownStyle=ComboBoxStyle.DropDownList},reason=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    public string Reference=>reference.Text.Trim();public string StandardValue=>value.Text;public string ComponentType=>type.Text;public TestState State=>state.SelectedIndex==0?TestState.Untestable:TestState.Testable;public string ReasonCode=>(reason.SelectedItem as ReasonChoice)?.Code??"";public string Note=>note.Text;public string Justification=>why.Text.Trim();public string Reviewer=>reviewer.Text.Trim();public string ProgramEvidence=>evidence.Text.Trim();
    sealed record ReasonChoice(string Code,string Text){public override string ToString()=>Text;}
    public EditForm(Component c,RuleConfig cfg)
    {
        Text=$"人工修订 · 源行 {c.SourceRow} · {c.RawReference}";Width=800;Height=540;StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(16),AutoSize=true};layout.ColumnStyles.Add(new(SizeType.Absolute,105));layout.ColumnStyles.Add(new(SizeType.Percent,100));Controls.Add(layout);
        type.Items.AddRange(cfg.PrefixTypes.Values.Distinct().Cast<object>().ToArray());type.SelectedItem=c.Type;state.Items.AddRange(new object[]{"不可测","可测"});state.SelectedIndex=c.State==TestState.Testable?1:0;
        foreach(var r in cfg.Reasons)reason.Items.Add(new ReasonChoice(r.Key,r.Value));reason.SelectedItem=reason.Items.Cast<ReasonChoice>().FirstOrDefault(x=>x.Code==c.ReasonCode);reference.Text=c.Reference;value.Text=c.Value;note.Text=c.Note;evidence.Text=c.ProgramEvidence;
        (string,Control)[] rows=[("规范位号",reference),("类型",type),("标准值",value),("检测状态",state),("盲点原因",reason),("备注",note),("测试程序证据",evidence),("修改人（必填）",reviewer),("修改理由（必填）",why)];
        for(int i=0;i<rows.Length;i++){layout.RowStyles.Add(new(SizeType.Absolute,40));layout.Controls.Add(new Label{Text=rows[i].Item1,AutoSize=true},0,i);rows[i].Item2.Dock=DockStyle.Fill;layout.Controls.Add(rows[i].Item2,1,i);}
        var save=new Button{Text="确认并记录修改",AutoSize=true};save.Click+=(_,_)=>{if(Justification.Length==0||Reviewer.Length==0){MessageBox.Show("请填写修改人和修改理由");return;}DialogResult=DialogResult.OK;};layout.Controls.Add(save,1,rows.Length);
    }
}
