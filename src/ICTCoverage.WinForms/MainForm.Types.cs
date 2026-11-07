using ICTCoverage.Core;
namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    readonly DataGridView typeGrid=AppTheme.Grid(),typeHistory=AppTheme.Grid();
    readonly TextBox typeSearch=new(){Width=160,PlaceholderText="搜索分类 / 前缀"};
    readonly CheckBox deletedTypes=new(){Text="显示已删除",AutoSize=true};
    Control BuildTypes()
    {
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.Controls.Add(AppTheme.Label("维护元件分类和基础前缀。删除可恢复，原始规则及修改记录保留。",10),0,0);
        var actions=AppTheme.Flow();actions.Controls.AddRange([typeSearch,AppTheme.Button("新增分类",AppTheme.Green,async(_,_)=>await EditType(true)),AppTheme.Button("编辑分类",AppTheme.Blue,async(_,_)=>await EditType(false)),AppTheme.Button("删除分类",AppTheme.Orange,async(_,_)=>await ArchiveType(true)),AppTheme.Button("恢复分类",AppTheme.Muted,async(_,_)=>await ArchiveType(false)),deletedTypes]);panel.Controls.Add(actions,0,1);
        var tabs=new TabControl{Dock=DockStyle.Fill};var names=new TabPage("元件分类");names.Controls.Add(typeGrid);var history=new TabPage("修改记录");history.Controls.Add(typeHistory);tabs.TabPages.AddRange([names,history]);panel.Controls.Add(tabs,0,2);
        typeSearch.TextChanged+=(_,_)=>RefreshTypes();deletedTypes.CheckedChanged+=(_,_)=>RefreshTypes();return panel;
    }
    void RefreshTypes()
    {
        string search=typeSearch.Text.Trim();typeGrid.DataSource=cfg.AllTypes.Where(t=>t!="待确认"&&(deletedTypes.Checked||!cfg.ArchivedTypes.Contains(t))).Select(t=>new{分类名称=t,基础前缀=string.Join("、",cfg.PrefixTypes.Where(p=>p.Value==t).Select(p=>p.Key)),状态=cfg.ArchivedTypes.Contains(t)?"已删除":"启用",关联规则=cfg.Rules.Count(r=>r.Types.Contains(t))}).Where(t=>t.分类名称.Contains(search,StringComparison.OrdinalIgnoreCase)||t.基础前缀.Contains(search,StringComparison.OrdinalIgnoreCase)).ToList();
        if(typeGrid.Columns.Contains("基础前缀"))typeGrid.Columns["基础前缀"].FillWeight=250;
        typeHistory.DataSource=cfg.TypeChanges.AsEnumerable().Reverse().Select(c=>new{时间=c.Time.ToLocalTime().ToString("MM-dd HH:mm"),操作=c.Action,原分类=c.Before,分类名称=c.After,前缀=c.Prefixes,修改人=c.Reviewer,说明=c.Justification}).ToList();
    }
    async Task EditType(bool create)
    {
        string? name=create?null:typeGrid.CurrentRow?.Cells["分类名称"].Value as string;if(!create&&name==null)return;
        using var editor=new TypeEditorForm(name,name==null?"":string.Join("、",cfg.PrefixTypes.Where(p=>p.Value==name).Select(p=>p.Key)));
        if(editor.ShowDialog(this)!=DialogResult.OK)return;
        try{await ApplyRules(TypeManagementService.Upsert(cfg,name,editor.TypeName,editor.Prefixes,editor.Reviewer,editor.Justification));}catch(Exception ex){MessageBox.Show(this,ex.Message,"分类未保存");}
    }
    async Task ArchiveType(bool archive)
    {
        if(typeGrid.CurrentRow?.Cells["分类名称"].Value is not string name)return;
        using var editor=new ChangeReasonForm(archive?"删除分类（可恢复）":"恢复分类");if(editor.ShowDialog(this)!=DialogResult.OK)return;
        try{await ApplyRules(TypeManagementService.Archive(cfg,name,archive,editor.Reviewer,editor.Justification));}catch(Exception ex){MessageBox.Show(this,ex.Message,"分类未保存");}
    }
}

internal sealed class TypeEditorForm : EditorBase
{
    readonly TextBox name=new(),prefixes=new(),reviewer=new(){Text=Environment.UserName},why=new(){Text="维护元件分类"};
    public string TypeName=>name.Text.Trim();
    public string[] Prefixes=>prefixes.Text.Split([',','，','、',';','；',' ','/','.'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    public string Reviewer=>reviewer.Text.Trim();public string Justification=>why.Text.Trim();
    public TypeEditorForm(string? original,string prefixText):base(original==null?"新增元件分类":"编辑元件分类",380)
    {
        name.Text=original??"";prefixes.Text=prefixText;Add("分类名称",name);Add("基础前缀",prefixes);Add("修改人",reviewer);Add("修改说明",why);
        SaveButton(()=>{if(TypeName.Length==0||Reviewer.Length==0||Justification.Length==0)throw new InvalidDataException("请填写分类名称、修改人和说明");});
    }
}
