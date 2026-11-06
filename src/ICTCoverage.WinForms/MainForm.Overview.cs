using ICTCoverage.Core;

namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    readonly List<OverviewCard> overview=[];
    readonly ComboBox groupReason=new(){Width=285,DropDownStyle=ComboBoxStyle.DropDownList,DropDownWidth=520};
    readonly Button confirmGroupReason=AppTheme.Button("确认本组原因",AppTheme.Orange,(_,_)=>{});
    readonly LinkLabel referenceToggle=new(){Text="查看本组位号",AutoSize=true,Margin=new Padding(12,12,4,0),Font=new Font("Microsoft YaHei UI",10.5f)};
    TableLayoutPanel? previewLayout;bool? referenceExpanded;
    bool onlyCausePending;
    sealed record ReasonChoice(string Code,string Text){public override string ToString()=>Text;}
    bool CausePending(Component c)=>c.State==TestState.Untestable&&!cfg.IsReasonActive(c.OutputReasonCode);
    Control BuildOverview()
    {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5};string[] names=["全部元件","不可测 Х","可测 √","不适用 ∕","原因待选择"];Color[] colors=[AppTheme.Ink,AppTheme.Blue,AppTheme.Green,AppTheme.Muted,AppTheme.Orange];
        for(int i=0;i<5;i++){row.ColumnStyles.Add(new(SizeType.Percent,20));int item=i;var card=new OverviewCard(names[i],colors[i],()=>ChooseOverview(item));overview.Add(card);row.Controls.Add(card,i,0);}return row;
    }
    void ChooseOverview(int item)
    {
        if(analysis==null)return;
        refreshing=true;onlyCausePending=item==4;TestState? state=item switch{1 or 4=>TestState.Untestable,2=>TestState.Testable,3=>TestState.NotApplicable,_=>null};stateFilter.SelectedItem=stateFilter.Items.Cast<StatusChoice>().Single(c=>c.State==state);refreshing=false;
        ShowPage("分类筛选 / 复制");RefreshResults();
    }
    void RefreshOverview(List<Component> scope)
    {
        var stats=InspectionStatistics.From(scope);int[] values=[stats.Total,stats.Untestable,stats.Testable,stats.NotApplicable,scope.Count(CausePending)];var selected=(stateFilter.SelectedItem as StatusChoice)?.State;
        int active=onlyCausePending?4:selected switch{TestState.Untestable=>1,TestState.Testable=>2,TestState.NotApplicable=>3,_=>0};
        for(int i=0;i<overview.Count;i++)overview[i].Update(analysis==null?null:values[i],i==active);
    }
    Control BuildGroupReason()
    {
        var row=AppTheme.Flow();row.Controls.AddRange([AppTheme.Label("本组原因",11,true),groupReason,confirmGroupReason,referenceToggle]);confirmGroupReason.Click+=(_,_)=>ConfirmGroupReason();referenceToggle.LinkClicked+=(_,_)=>{referenceExpanded=!refs.Visible;UpdatePreviewLayout();};return row;
    }
    void UpdatePreviewLayout()
    {
        if(previewLayout==null)return;bool show=referenceExpanded??previewLayout.Height>=410;refs.Visible=show;previewLayout.RowStyles[1].SizeType=SizeType.Percent;previewLayout.RowStyles[1].Height=show?65:100;previewLayout.RowStyles[3].SizeType=show?SizeType.Percent:SizeType.Absolute;previewLayout.RowStyles[3].Height=show?35:0;referenceToggle.Text=show?"收起本组位号":"查看本组位号";
    }
    void RefreshReasonChoices()
    {
        groupReason.Items.Clear();groupReason.Items.Add(new ReasonChoice("","请选择盲点原因…"));foreach(var p in cfg.ActiveReasons)groupReason.Items.Add(new ReasonChoice(p.Key,p.Value));groupReason.SelectedIndex=0;
    }
    void ShowGroupReason(BlindGroup? group)
    {
        groupReason.Enabled=group!=null&&group.Items.All(c=>c.State==TestState.Untestable)&&!group.HasDataIssues;
        string code=group?.ReasonCode??"";groupReason.SelectedItem=groupReason.Items.Cast<ReasonChoice>().FirstOrDefault(r=>r.Code==code)??groupReason.Items.Cast<ReasonChoice>().FirstOrDefault();
        confirmGroupReason.Enabled=groupReason.Enabled;
    }
    void ConfirmGroupReason()
    {
        var group=SelectedGroup();if(analysis==null||group==null||group.HasDataIssues||group.Items.Any(c=>c.State!=TestState.Untestable))return;
        if(groupReason.SelectedItem is not ReasonChoice reason||reason.Code.Length==0){status.Text="请先从“本组原因”中选择一项，再确认。";groupReason.Focus();groupReason.DroppedDown=true;return;}
        try
        {
            int changed=analysis.Edits.Count;foreach(var c in group.Items)AnalysisEngine.Edit(analysis,c,c.Reference,c.Type,c.Value,c.SourceState,reason.Code,c.Note,"确认本组盲点原因",cfg);
            SaveSession();RefreshResults();status.Text=$"已确认 {analysis.Edits.Count-changed} 个元件的原因：{cfg.ReasonText(reason.Code)}。";
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"原因未保存");}
    }
}
