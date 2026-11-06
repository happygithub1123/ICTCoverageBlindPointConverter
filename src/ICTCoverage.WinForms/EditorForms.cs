using ICTCoverage.Core;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ICTCoverage.WinForms;

internal class EditorBase : Form
{
    readonly TableLayoutPanel fields=new(){Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(12)};
    protected readonly FlowLayoutPanel actions=AppTheme.Flow();
    int row;
    protected EditorBase(string title,int height=500)
    {
        Text=title;Width=760;Height=height;MinimumSize=new Size(640,380);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);BackColor=AppTheme.Background;AutoScaleMode=AutoScaleMode.Dpi;
        fields.ColumnStyles.Add(new(SizeType.Absolute,140));fields.ColumnStyles.Add(new(SizeType.Percent,100));var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};scroll.Controls.Add(fields);Controls.Add(scroll);actions.Dock=DockStyle.Bottom;actions.Padding=new Padding(12);Controls.Add(actions);actions.Controls.Add(AppTheme.Button("取消",AppTheme.Muted,(_,_)=>DialogResult=DialogResult.Cancel));
    }
    protected void Add(string label,Control c){fields.RowStyles.Add(new(SizeType.AutoSize));fields.Controls.Add(AppTheme.Label(label,9),0,row);c.Dock=DockStyle.Fill;c.Margin=new Padding(5,7,5,7);fields.Controls.Add(c,1,row++);}
    protected void SaveButton(Action action)=>actions.Controls.Add(AppTheme.Button("保存并应用",AppTheme.Green,(_,_)=>{try{action();DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message,"请检查填写内容");}}));
}

internal sealed class CauseForm : EditorBase
{
    readonly ComboBox type=new(){DropDownStyle=ComboBoxStyle.DropDownList},reason=new(){DropDownStyle=ComboBoxStyle.DropDown};
    readonly CheckBox saveRule=new(){Text="将此原因保存为规则",AutoSize=true};string selectedCode="";
    public bool SaveAsRule=>saveRule.Checked;public string? NewReasonText{get;private set;}readonly TextBox value=new(),note=new(),reviewer=new(){Text=Environment.UserName},why=new(){Text="选择盲点原因"};
    readonly RuleConfig cfg;
    public string ComponentType=>type.Text;public string StandardValue=>value.Text;public string Note=>note.Text;public string Reviewer=>reviewer.Text.Trim();public string Justification=>why.Text.Trim();public string ReasonCode=>selectedCode;
    public CauseForm(Component c,RuleConfig rules,InspectionKind inspection=InspectionKind.Missing):base("选择盲点原因 · "+inspection.Name()+" · "+c.Reference,560)
    {
        cfg=rules;Add("元件 / 来源",new Label{Text=$"{c.Reference}  ·  第{c.SourceRow}行  ·  Coverage = X",AutoSize=true});type.Items.AddRange(cfg.KnownTypes.Cast<object>().ToArray());type.SelectedItem=c.Type;reason.Items.AddRange(cfg.ActiveReasons.Select(p=>p.Value).Cast<object>().ToArray());reason.SelectedItem=cfg.ReasonText(c.ReasonCode);value.Text=c.Value;note.Text=c.Note;
        Add("元件类型",type);Add("标准值",value);Add("盲点原因",reason);Add("备注",note);Add("修改人",reviewer);Add("选择说明",why);Add("复用此原因",saveRule);SaveButton(()=>{string text=reason.Text.Trim();if(ComponentType.Length==0||text.Length==0||StandardValue.Trim().Length==0||Reviewer.Length==0||Justification.Length==0)throw new InvalidDataException("请选择类型、原因并填写标准值和修改人");var found=cfg.ActiveReasons.FirstOrDefault(p=>p.Value==text);selectedCode=found.Key??("CUSTOM_REASON_"+Guid.NewGuid().ToString("N")[..8]);NewReasonText=found.Key==null?text:null;});
    }
}

internal sealed class ChangeReasonForm : EditorBase
{
    readonly TextBox reviewer=new(){Text=Environment.UserName},why=new();public string Reviewer=>reviewer.Text.Trim();public string Justification=>why.Text.Trim();
    public ChangeReasonForm(string title):base(title,300){Add("修改人",reviewer);Add("操作理由",why);SaveButton(()=>{if(Reviewer.Length==0||Justification.Length==0)throw new InvalidDataException("请填写修改人和操作理由");});}
}

internal sealed class RuleEditorForm : EditorBase
{
    readonly RuleConfig cfg;readonly ClassificationRule original;
    readonly ComboBox type=new(){DropDownStyle=ComboBoxStyle.DropDownList},reason=new(){DropDownStyle=ComboBoxStyle.DropDown};
    readonly TextBox prefixes=new(),suffix=new(),exclude=new(),value=new(),reference=new(),project=new(),source=new(),reviewer=new(){Text=Environment.UserName},why=new(),supersedes=new();
    readonly CheckedListBox inspectionScopes=new(){CheckOnClick=true,Height=84};readonly TextBox references=new(),displayValue=new();
    readonly NumericUpDown priority=new(){Minimum=0,Maximum=10000,Value=160};readonly CheckBox enabled=new(){Text="启用规则",Checked=true,AutoSize=true},reviewOnly=new(){Text="仅记录参考，不自动归因",AutoSize=true},noSuffix=new(){Text="不含任何分类后缀",AutoSize=true};
    public ClassificationRule Rule {get;private set;}=new();public string? NewReasonText{get;private set;}public string Reviewer=>reviewer.Text.Trim();public string Justification=>why.Text.Trim();
    static string? Optional(string s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();
    static string[] Tokens(string s)=>s.ToUpperInvariant().Split([',','，',' ','/',';','；'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    public RuleEditorForm(RuleConfig cfg,ClassificationRule? rule):this(cfg,rule,InspectionKind.Missing){}
    public RuleEditorForm(RuleConfig cfg,ClassificationRule? rule,InspectionKind inspection):base(rule==null?"新增分类规则":"编辑规则 · "+rule.RuleId,760)
    {
        this.cfg=cfg;original=rule==null?new(){RuleId=RuleManagementService.NewId(cfg),Stage="原因分类",Status="用户自定义",Source="用户自定义",Priority=160,Inspections=[inspection]}:JsonSerializer.Deserialize<ClassificationRule>(JsonSerializer.Serialize(rule,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;
        type.Items.AddRange(cfg.KnownTypes.Cast<object>().ToArray());if(original.Types.Length>0)type.SelectedItem=original.Types[0];reason.Items.AddRange(cfg.ActiveReasons.Select(p=>p.Value).Cast<object>().ToArray());reason.Text=!cfg.IsReasonActive(original.Reason)?"":cfg.ReasonText(original.Reason);prefixes.Text=string.Join(",",original.Prefixes);suffix.Text=original.Suffix??"";exclude.Text=string.Join(",",original.ExcludeSuffixes);value.Text=original.ExactValue??"";reference.Text=original.Reference??"";project.Text=original.Profile??"";source.Text=original.Source;supersedes.Text=string.Join(",",original.Supersedes);priority.Value=Math.Clamp(original.Priority,0,10000);enabled.Checked=original.Enabled;reviewOnly.Checked=original.ReviewOnly;
        foreach(var k in Enum.GetValues<InspectionKind>())inspectionScopes.Items.Add(k.Name(),original.Inspections.Contains(k));references.Text=string.Join(",",original.References);displayValue.Text=original.DisplayValue??"";
        noSuffix.Checked=original.NoOtherSuffixes;Add("规则编号",new Label{Text=original.RuleId,AutoSize=true});Add("适用检测项目",inspectionScopes);Add("元件类型",type);Add("前缀（逗号分隔）",prefixes);Add("包含后缀",suffix);Add("排除后缀",exclude);Add("无后缀条件",noSuffix);Add("精确标准值",value);Add("指定元件位号",reference);Add("位号集合（可选）",references);Add("输出标准值（可选）",displayValue);Add("盲点原因（可新建）",reason);Add("优先级（小数值优先）",priority);Add("覆盖规则编号",supersedes);Add("指定项目（空=通用）",project);Add("来源",source);Add("启用",enabled);Add("参考规则",reviewOnly);Add("修改人",reviewer);Add("修改理由",why);SaveButton(BuildRule);
    }
    void BuildRule()
    {
        if(type.SelectedIndex<0||source.Text.Trim().Length==0||Reviewer.Length==0||Justification.Length==0)throw new InvalidDataException("请填写类型、来源、修改人和修改理由");
        var ps=Tokens(prefixes.Text);if(ps.Any(p=>!Regex.IsMatch(p,"^[A-Z]+$")))throw new InvalidDataException("前缀只填写字母，例如 R,RA,RB");
        Rule=JsonSerializer.Deserialize<ClassificationRule>(JsonSerializer.Serialize(original,RuleConfig.JsonOptions),RuleConfig.JsonOptions)!;Rule.Types=[type.Text];Rule.Prefixes=ps;Rule.Suffix=Optional(suffix.Text)?.TrimStart('/').ToUpperInvariant();Rule.ExcludeSuffixes=Tokens(exclude.Text);Rule.ExactValue=Optional(value.Text);Rule.Reference=Optional(reference.Text)?.ToUpperInvariant();Rule.Priority=(int)priority.Value;Rule.Profile=Optional(project.Text);Rule.Scope=Rule.Profile==null?"report_profile":"project";Rule.Source=source.Text.Trim();Rule.Enabled=enabled.Checked;Rule.ReviewOnly=reviewOnly.Checked;Rule.Supersedes=Tokens(supersedes.Text);
        Rule.Inspections=Enum.GetValues<InspectionKind>().Where(k=>inspectionScopes.CheckedItems.Contains(k.Name())).ToArray();if(Rule.Inspections.Length==0)throw new InvalidDataException("请选择适用检测项目");Rule.References=Tokens(references.Text);Rule.DisplayValue=Optional(displayValue.Text);
        Rule.NoOtherSuffixes=noSuffix.Checked;if(Rule.NoOtherSuffixes&&Rule.Suffix!=null)throw new InvalidDataException("无后缀条件与包含后缀不能同时设置");
        if(Rule.ReviewOnly){Rule.Reason="";return;}
        string text=reason.Text.Trim();if(text.Length==0)throw new InvalidDataException("请选择或输入盲点原因");var found=cfg.ActiveReasons.FirstOrDefault(p=>p.Value==text);Rule.Reason=found.Key??("CUSTOM_REASON_"+Guid.NewGuid().ToString("N")[..8]);NewReasonText=found.Key==null?text:null;
    }
}

internal sealed class ExportResultForm : EditorBase
{
    public ExportResultForm(string path):base("盲点表已生成",280)
    {
        Add("完成",new Label{Text="已生成盲点表，并通过位号数量校验。",AutoSize=true});Add("文件",new TextBox{ReadOnly=true,Text=path});
        actions.Controls.Add(AppTheme.Button("打开盲点表",AppTheme.Green,(_,_)=>Open(path)));actions.Controls.Add(AppTheme.Button("打开所在文件夹",AppTheme.Blue,(_,_)=>Open(Path.GetDirectoryName(path)!)));
    }
    void Open(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
}

public sealed class ChoiceForm<T> : Form
{
    readonly ListBox list=new(){Dock=DockStyle.Fill};public T? Selected=>list.SelectedItem is T value?value:default;
    public ChoiceForm(string title,List<T> items){Text=title;Width=850;Height=260;StartPosition=FormStartPosition.CenterParent;list.Items.AddRange(items.Cast<object>().ToArray());list.SelectedIndex=0;Controls.Add(list);var button=AppTheme.Button("使用选中项",AppTheme.Blue,(_,_)=>DialogResult=DialogResult.OK);button.Dock=DockStyle.Bottom;Controls.Add(button);AcceptButton=button;}
}
