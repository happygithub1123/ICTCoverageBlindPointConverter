using ICTCoverage.Core;
using System.Text.Json;
namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    readonly LinkLabel copyToggle=new(){Text="展开复制工具与本组位号",AutoSize=true,Margin=new Padding(5,10,5,8)};
    void CollapseCopyTools(){copyExpanded=false;copyTools.Visible=false;copyLayout.RowStyles[3].Height=0;copyToggle.Text="展开复制工具与本组位号";}
    Control BuildCopy()
    {
        copyLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};copyLayout.RowStyles.Add(new(SizeType.AutoSize));copyLayout.RowStyles.Add(new(SizeType.Percent,100));copyLayout.RowStyles.Add(new(SizeType.AutoSize));copyLayout.RowStyles.Add(new(SizeType.Absolute,0));
        var hint=AppTheme.Label("仅整理Х记录；全部检测项目时按缺件准备复制。",10);copyLayout.Controls.Add(hint,0,0);copyLayout.Controls.Add(copyGrid,0,1);copyToggle.LinkClicked+=(_,_)=>{copyExpanded=!copyExpanded;copyTools.Visible=copyExpanded;copyLayout.RowStyles[3].Height=copyExpanded?190:0;copyToggle.Text=copyExpanded?"收起复制工具与本组位号":"展开复制工具与本组位号";};copyLayout.Controls.Add(copyToggle,0,2);
        var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};tools.RowStyles.Add(new(SizeType.Percent,100));tools.RowStyles.Add(new(SizeType.AutoSize));tools.Controls.Add(refs,0,0);var buttons=AppTheme.Flow();buttons.Controls.AddRange([copyOne,copyFour]);tools.Controls.Add(buttons,0,1);copyTools.Controls.Add(tools);copyLayout.Controls.Add(copyTools,0,3);return copyLayout;
    }
    void RefreshCopy()
    {
        var kind=selectedKind??InspectionKind.Missing;if(report==null||!report.Analyses.TryGetValue(kind,out var a)){groups=[];copyGrid.DataSource=null;ShowGroup();return;}
        groups=AnalysisEngine.Group(AnalysisEngine.Filter(a,CurrentFilter() with{OnlyUntestable=true,Status=TestState.Untestable},cfg));copyGrid.DataSource=groups.Select((g,i)=>new{索引=i,检测项目=kind.Name(),元件类型=g.Items[0].GroupCategory,元件位置=ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace('\n',' '),数量=g.Count,标准值=g.Value,盲点原因=cfg.ReasonText(g.ReasonCode)}).ToList();if(copyGrid.Columns.Contains("索引"))copyGrid.Columns["索引"].Visible=false;if(copyGrid.Columns.Contains("元件位置"))copyGrid.Columns["元件位置"].FillWeight=240;ShowGroup();
    }
    BlindGroup? SelectedGroup()=>copyGrid.CurrentRow?.Cells["索引"].Value is int i&&i>=0&&i<groups.Count?groups[i]:null;
    void ShowGroup(){var g=SelectedGroup();refs.Text=g==null?"":ReferenceFormatter.Format(g.Items.Select(c=>c.Reference),(int)width.Value).Replace("\n","\r\n");copyOne.Enabled=copyFour.Enabled=g!=null&&!g.HasDataIssues;}
    void CopyGroup(bool four){try{var g=SelectedGroup();if(g==null)return;ClipboardService.Copy(ClipboardService.GroupCells(g,cfg,(int)width.Value,four));status.Text=$"已复制{g.Count}个位号"+(four?"及数量、标准值和原因，粘贴到Excel B列。":"，粘贴到Excel单元格。");}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
    Control BuildIssues()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.RowStyles.Add(new(SizeType.AutoSize));
        var actions=AppTheme.Flow();actions.Controls.AddRange([selectAllIssues,batchReason,AppTheme.Button("批量确认原因",AppTheme.Orange,async(_,_)=>await ConfirmBatch()),AppTheme.Button("单条校准",AppTheme.Blue,async(_,_)=>await ChooseReason())]);p.Controls.Add(actions,0,0);p.Controls.Add(issues,0,1);p.Controls.Add(AppTheme.Label("勾选后批量修改原因；文件数据问题不参与批量操作。",10),0,2);
        selectAllIssues.CheckedChanged+=(_,_)=>SelectAllIssues();return p;
    }
    void RefreshBatchReasons(){batchReason.Items.Clear();batchReason.Items.Add(new ReasonChoice("","请选择盲点原因…"));foreach(var p in cfg.ActiveReasons)batchReason.Items.Add(new ReasonChoice(p.Key,p.Value));batchReason.SelectedIndex=0;}
    void RefreshIssues(List<ReportRow> scope)
    {
        issueTargets.Clear();if(report==null)return;
        foreach(var pair in report.Analyses.Where(p=>!selectedKind.HasValue||p.Key==selectedKind))foreach(var c in AnalysisEngine.Filter(pair.Value,CurrentFilter(),cfg).Where(c=>c.Pending))issueTargets.Add((new(pair.Key,c.Sheet,c.SourceRow,report.SourceFingerprint),c));
        checkedIssues.Clear();selectAllIssues.Checked=false;issues.RowCount=issueTargets.Count;issues.Invalidate();
        nav["异常待确认"].Text="异常待确认"+(issueTargets.Count>0?" "+issueTargets.Count:"");
    }
    async Task ConfirmBatch()
    {
        issues.EndEdit();var targets=checkedIssues.Where(CanSelectIssue).Select(i=>issueTargets[i].Target).ToArray();
        if(batchReason.SelectedItem is not ReasonChoice reason||reason.Code.Length==0||targets.Length==0){status.Text="请勾选记录并选择一个原因。";return;}
        string range=string.Join("、",targets.GroupBy(t=>t.Inspection).Select(g=>g.Key.Name()+" "+g.Count()+"条"));if(MessageBox.Show(this,$"将{targets.Length}条记录（{range}）的原因设置为：\n{reason.Text}\n检测符号和标准值保持不变。","确认批量修改",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
        await RunBusy(async()=>{status.Text="正在保存批量原因修改…";await CommitBatch(targets,reason.Code);status.Text=$"已批量确认{targets.Length}条记录，最终结果已保存。";});
    }
    internal async Task CommitBatch(IEnumerable<CauseTarget> targets,string reason,string? destination=null)
    {
        var original=report??throw new InvalidOperationException("请导入报告");var selection=targets.ToArray();
        var next=await Task.Run(()=>{var prepared=BatchReasonService.Prepare(original,selection,reason,cfg);BatchReasonService.Save(prepared,destination??SessionPath());return prepared;});
        report=next;ActivateInspection();
    }
    async Task ChooseReason()
    {
        if(report==null||issues.CurrentRow?.Cells["索引"].Value is not int i)return;var target=issueTargets[i];if(target.Item.SourceState!=TestState.Untestable){MessageBox.Show(this,"未知符号需要检查源报告，软件不更改检测结果。");return;}
        using var form=new CauseForm(target.Item,cfg,target.Target.Inspection);if(form.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            // Read editor controls on the UI thread, then prepare and persist off-thread.
            string code=form.ReasonCode,componentType=form.ComponentType,value=form.StandardValue,annotation=form.Note,why=form.Justification,reviewer=form.Reviewer;
            string? newReason=form.NewReasonText;var original=report;var originalCfg=cfg;Component? edited=null;
            await RunBusy(async()=>
            {
                status.Text="正在保存原因校准…";
                var result=await Task.Run(()=>
                {
                    var nextCfg=newReason==null?originalCfg:ReasonManagementService.Upsert(originalCfg,code,newReason,reviewer,why);
                    var next=JsonSerializer.Deserialize<CoverageReport>(JsonSerializer.Serialize(original,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;var a=next.Analyses[target.Target.Inspection];
                    var item=a.Components.Single(c=>c.Sheet==target.Target.Sheet&&c.SourceRow==target.Target.SourceRow);
                    AnalysisEngine.Edit(a,item,item.Reference,componentType,value,item.SourceState,code,annotation,why,nextCfg,reviewer);
                    RuleManagementService.Save(nextCfg,RulePath);BatchReasonService.Save(next,SessionPath(next,nextCfg));return(Config:nextCfg,Report:next,Item:item);
                });
                cfg=result.Config;report=result.Report;edited=result.Item;RefreshChoices();ActivateInspection();status.Text="原因校准已保存。";
            });
            if(edited==null)return;var item=edited;var a=report.Analyses[target.Target.Inspection];
            if(form.SaveAsRule){var rule=new ClassificationRule{RuleId=RuleManagementService.NewId(cfg),Stage="原因分类",Status="人工校准",Source="人工原因选择："+Path.GetFileName(report.SourcePath)+" 第"+item.SourceRow+"行",Inspections=Enum.GetValues<InspectionKind>(),Types=[item.Type],Reference=item.Reference,ExactValue=item.RawValue,DisplayValue=item.Value,Reason=item.ReasonCode,Priority=Math.Max(0,item.MatchedRules.Select(r=>r.Priority).DefaultIfEmpty(161).Min()-1),Supersedes=item.MatchedRules.Select(r=>r.RuleId).ToArray(),Profile=ProjectIdentification.Identity(a)};rule.Scope=string.IsNullOrEmpty(rule.Profile)?"report_profile":"project";if(rule.Profile=="")rule.Profile=null;EditPreparedRule(rule);}
        }catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    Control BuildRules()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.Controls.Add(projectInfo,0,0);var actions=AppTheme.Flow();actions.Controls.AddRange([AppTheme.Button("新增规则",AppTheme.Green,(_,_)=>EditRule(true)),AppTheme.Button("编辑规则",AppTheme.Blue,(_,_)=>EditRule(false)),AppTheme.Button("删除规则",AppTheme.Orange,async(_,_)=>await ArchiveRule(true)),AppTheme.Button("恢复规则",AppTheme.Muted,async(_,_)=>await ArchiveRule(false)),archived]);p.Controls.Add(actions,0,1);p.Controls.Add(rulesGrid,0,2);return p;
    }
    Control BuildRuleOverview(){var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));p.Controls.Add(AppTheme.Label("历史依据保留，当前检测只读取Coverage结果。",10),0,0);p.Controls.Add(ruleList,0,1);return p;}
    void RefreshRules(){rulesGrid.DataSource=cfg.Rules.Where(RuleManagementService.CanEdit).Where(r=>archived.Checked||!r.Archived).OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,类型=string.Join("/",r.Types),后缀=r.Suffix??"不限",标准值=r.ExactValue??"不限",优先级=r.Priority,状态=r.Archived?"已删除":r.Enabled?"启用":"停用",原因=cfg.ReasonText(r.Reason),项目=r.Profile??"通用"}).ToList();ruleList.DataSource=cfg.Rules.OrderBy(r=>r.Priority).Select(r=>new{编号=r.RuleId,阶段=r.Stage,优先级=r.Priority,当前执行=RulePresentation.CurrentExecution(r),项目=r.Profile??"通用",历史依据=RulePresentation.HistoricalBasis(r),来源=r.Source}).ToList();}
    void EditRule(bool create){var original=create?null:cfg.Rules.FirstOrDefault(r=>r.RuleId==rulesGrid.CurrentRow?.Cells["编号"].Value as string);if(!create&&original==null)return;EditPreparedRule(original,create);}
    async void EditPreparedRule(ClassificationRule? rule,bool? create=null){using var form=new RuleEditorForm(cfg,rule);if(form.ShowDialog(this)!=DialogResult.OK)return;try{await ApplyRules(RuleManagementService.Update(cfg,form.Rule,form.Reviewer,form.Justification,create??true,form.NewReasonText));}catch(Exception ex){MessageBox.Show(this,ex.Message,"规则未保存");}}
    async Task ArchiveRule(bool archive){if(rulesGrid.CurrentRow?.Cells["编号"].Value is not string id)return;using var form=new ChangeReasonForm(archive?"删除规则（可恢复）":"恢复规则");if(form.ShowDialog(this)!=DialogResult.OK)return;try{await ApplyRules(RuleManagementService.Archive(cfg,id,archive,form.Reviewer,form.Justification));}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
}
