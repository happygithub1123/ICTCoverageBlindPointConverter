namespace ICTCoverage.WinForms;

internal sealed class ReasonEditorForm : EditorBase
{
    readonly TextBox name=new(){Multiline=true,Height=95,ScrollBars=ScrollBars.Vertical},reviewer=new(){Text=Environment.UserName},why=new(){Text="维护盲点原因"};
    public string ReasonName=>name.Text.Trim();public string Reviewer=>reviewer.Text.Trim();public string Justification=>why.Text.Trim();
    public ReasonEditorForm(string? original):base(original==null?"新增盲点原因":"修改盲点原因",390)
    {
        name.Text=original??"";Add("盲点原因名称",name);Add("修改人",reviewer);Add("修改说明",why);
        SaveButton(()=>{if(ReasonName.Length==0||Reviewer.Length==0||Justification.Length==0)throw new InvalidDataException("请填写原因名称、修改人和说明");});
    }
}
