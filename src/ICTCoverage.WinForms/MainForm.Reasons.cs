using ICTCoverage.Core;

namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    readonly DataGridView reasonGrid=AppTheme.Grid(),reasonHistory=AppTheme.Grid();
    readonly TextBox reasonSearch=new(){Width=180,PlaceholderText="搜索盲点原因 / 编号"};
    readonly TabControl reasonTabs=new(){Dock=DockStyle.Fill};
    readonly CheckBox deletedReasons=new(){Text="显示已删除",AutoSize=true,Margin=new Padding(10,12,4,0)};
    Control BuildReasons()
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.AutoSize));p.RowStyles.Add(new(SizeType.Percent,100));
        p.Controls.Add(AppTheme.Label("维护盲点原因，保存后同步到原因选择和当前报告。删除可恢复，未知原因输出“待确定”。",9),0,0);
        var actions=AppTheme.Flow();actions.Controls.AddRange([reasonSearch,AppTheme.Button("新增原因",AppTheme.Green,async(_,_)=>await EditBlindReason(true)),AppTheme.Button("修改名称",AppTheme.Blue,async(_,_)=>await EditBlindReason(false)),AppTheme.Button("删除原因",AppTheme.Orange,async(_,_)=>await ArchiveBlindReason(true)),AppTheme.Button("恢复原因",AppTheme.Muted,async(_,_)=>await ArchiveBlindReason(false)),deletedReasons]);p.Controls.Add(actions,0,1);
        var names=new TabPage("盲点原因"){BackColor=Color.White,Padding=new Padding(3)};names.Controls.Add(reasonGrid);var history=new TabPage("操作记录"){BackColor=Color.White,Padding=new Padding(3)};history.Controls.Add(reasonHistory);reasonTabs.TabPages.AddRange([names,history]);p.Controls.Add(reasonTabs,0,2);
        reasonSearch.TextChanged+=(_,_)=>RefreshReasons();deletedReasons.CheckedChanged+=(_,_)=>RefreshReasons();return p;
    }
    void RefreshReasons()
    {
        string search=reasonSearch.Text.Trim();
        reasonGrid.DataSource=cfg.Reasons.Where(p=>deletedReasons.Checked||cfg.IsReasonActive(p.Key)).Where(p=>p.Key.Contains(search,StringComparison.OrdinalIgnoreCase)||p.Value.Contains(search,StringComparison.OrdinalIgnoreCase)).Select(p=>new{编号=p.Key,盲点原因=p.Value,状态=cfg.ArchivedReasons.Contains(p.Key)?"已删除":"可选择",关联规则=cfg.Rules.Count(r=>r.Reason==p.Key)}).ToList();
        if(reasonGrid.Columns.Contains("盲点原因"))reasonGrid.Columns["盲点原因"].FillWeight=280;
        reasonHistory.DataSource=cfg.ReasonChanges.AsEnumerable().Reverse().Where(c=>c.Code.Contains(search,StringComparison.OrdinalIgnoreCase)||c.Before.Contains(search,StringComparison.OrdinalIgnoreCase)||c.After.Contains(search,StringComparison.OrdinalIgnoreCase)).Select(c=>new{时间=c.Time.ToLocalTime().ToString("MM-dd HH:mm:ss"),操作=c.Action,原名称=c.Before,盲点原因=c.After,修改人=c.Reviewer,说明=c.Justification}).ToList();
    }
    string? SelectedReason()=>reasonGrid.CurrentRow?.Cells["编号"].Value as string;
    async Task EditBlindReason(bool create)
    {
        string? code=create?null:SelectedReason();if(!create&&code==null)return;
        using var form=new ReasonEditorForm(create?null:cfg.Reasons[code!]);if(form.ShowDialog(this)!=DialogResult.OK)return;
        try{await ApplyRules(ReasonManagementService.Upsert(cfg,code,form.ReasonName,form.Reviewer,form.Justification));}catch(Exception ex){MessageBox.Show(this,ex.Message,"原因未保存");}
    }
    async Task ArchiveBlindReason(bool archive)
    {
        string? code=SelectedReason();if(code==null||cfg.ArchivedReasons.Contains(code)==archive)return;
        try{await ApplyRules(ReasonManagementService.Archive(cfg,code,archive,Environment.UserName,archive?"在盲点设置中删除原因":"在盲点设置中恢复原因"));}catch(Exception ex){MessageBox.Show(this,ex.Message,"原因未保存");}
    }
}
