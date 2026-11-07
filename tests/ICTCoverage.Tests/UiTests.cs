using Xunit;
using ICTCoverage.WinForms;
using ICTCoverage.Core;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
namespace ICTCoverage.Tests;

[Collection("WinForms")]
public sealed class UiTests
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    static object? Call(object o,string name,params object?[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);
    static IEnumerable<Control> Children(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
    static void Screenshot(Form form,string name){Application.DoEvents();using var bmp=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(ConverterTests.Root,"outputs",name+".png"));}
    [Fact]public async Task MainForm_AllViewsCopyBatchRulesAndFullExport()
    {
        Exception? failure=null;var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();using var form=new MainForm(Path.Combine(ConverterTests.Root,"outputs","ui-v5-"+Guid.NewGuid().ToString("N")));form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);
                form.Shown+=async(_,_)=>
                {
                    try
                    {
                        Field<TextBox>(form,"input").Text=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();await (Task)Call(form,"ReadAsync")!;
                        var grid=Field<ReportTable>(form,"grid");var type=Field<ComboBox>(form,"type");var state=Field<ComboBox>(form,"stateFilter");var report=Field<CoverageReport>(form,"report");var cfg=Field<RuleConfig>(form,"cfg");
                        Assert.Equal(1202,grid.RowCount);Assert.Null(typeof(MainForm).GetField("selectedKind",Private)!.GetValue(form));Assert.Equal("全部",type.Text);Assert.Equal("全部状态",state.Text);Assert.Contains("待确认",type.Items.Cast<string>());Assert.Equal(8,ReportTable.ColumnNames.Length);
                        var nav=Field<Dictionary<string,Button>>(form,"nav");Assert.Equal(8,nav.Count);Assert.Contains("复制",nav.Keys);Assert.DoesNotContain("分类筛选 / 复制",nav.Keys);Assert.Contains("规则总览",nav.Keys);Assert.DoesNotContain("V2规则总览",nav.Keys);Assert.Contains(Children(form).OfType<Button>(),b=>b.Text=="导出盲点表");Assert.DoesNotContain(Children(form).OfType<Button>(),b=>b.Text.Contains("导出整板"));Assert.All(nav.Values,b=>Assert.True(b.Font.Size>=13));Assert.Contains(Children(form).OfType<Label>(),c=>c.Text.Contains("设计人员：王开心")&&c.Font.Size>=12);Assert.Contains(Children(form),c=>c.Text.Contains("本软件最终解释权归王开心"));Assert.DoesNotContain(Children(Field<Dictionary<string,Control>>(form,"pages")["分类筛选"]),c=>ReferenceEquals(c,Field<Button>(form,"copyOne")));
                        type.SelectedItem="待确认";Assert.InRange(grid.RowCount,1,1202);Call(form,"ResetFilters");Screenshot(form,"v5-分类筛选-大窗口");form.Size=new Size(1120,760);Screenshot(form,"v5-分类筛选-小窗口");Assert.True(grid.Height>=210,"主表需要保留主要显示空间");
                        type.SelectedItem="电阻";Field<TextBox>(form,"valueFilter").Text="0.Ω";Call(form,"ShowPage","复制");var copyGrid=Field<DataGridView>(form,"copyGrid");Assert.Single(copyGrid.Rows.Cast<DataGridViewRow>());Assert.Equal(165,copyGrid.Rows[0].Cells["数量"].Value);Assert.False(Field<Panel>(form,"copyTools").Visible);
                        var toggle=Children(form).OfType<LinkLabel>().Single(c=>c.Text=="展开复制工具与本组位号");typeof(LinkLabel).GetMethod("OnLinkClicked",Private)!.Invoke(toggle,[new LinkLabelLinkClickedEventArgs(toggle.Links[0])]);copyGrid.CurrentCell=copyGrid.Rows[0].Cells["元件位置"];Field<Button>(form,"copyFour").PerformClick();Assert.True(Clipboard.ContainsData(DataFormats.Html));Assert.Contains("R103.R104.",Clipboard.GetText());Screenshot(form,"v5-复制工具");
                        Call(form,"ChooseInspection",InspectionKind.Short);state.SelectedIndex=2;string dir=Path.Combine(ConverterTests.Root,"outputs","ui-v5-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string target=Path.Combine(dir,"整板盲点表.xlsx");await (Task)Call(form,"WriteBlindPointAsync",target,new ExportOptions("光板","成品"))!;Assert.Single(Directory.GetFiles(dir));ExcelExporter.Verify(target,report.Analyses[InspectionKind.Missing]);Call(form,"ShowPage","异常待确认");Assert.Empty(Field<DataGridView>(form,"issues").Rows.Cast<DataGridViewRow>());
                        Call(form,"ResetFilters");Call(form,"ShowPage","异常待确认");var issues=Field<DataGridView>(form,"issues");var targets=Field<List<(CauseTarget Target,Component Item)>>(form,"issueTargets").Where(t=>t.Item.SourceState==TestState.Untestable&&!t.Item.HasDataIssues).Take(2).Select(t=>t.Target).ToArray();Assert.Equal(2,targets.Length);
                        await (Task)Call(form,"CommitBatch",targets,"NO_POINT",null)!;report=Field<CoverageReport>(form,"report");Assert.Equal(2,ManualResults.Get(report,cfg).Count);Assert.All(Enumerable.Range(0,grid.RowCount).Where(r=>targets.Any(t=>report.Analyses[InspectionKind.Short].Components.Single(c=>c.SourceRow==t.SourceRow).Reference==grid.GetValue(r,"元件位置")?.ToString())),r=>Assert.Contains(cfg.ReasonText("NO_POINT"),grid.GetValue(r,"盲点原因")!.ToString()));Call(form,"ShowPage","人工修改日志");Assert.Equal(2,Field<DataGridView>(form,"log").Rows.Count);Call(form,"ShowPage","异常待确认");Assert.All(report.Analyses[InspectionKind.Short].Components.Where(c=>targets.Any(t=>t.SourceRow==c.SourceRow)),c=>Assert.Equal(TestState.Untestable,c.SourceState));
                        var remaining=Field<List<(CauseTarget Target,Component Item)>>(form,"issueTargets").First(t=>!t.Item.HasDataIssues).Target;var before=report;await Assert.ThrowsAsync<DirectoryNotFoundException>(async()=>await (Task)Call(form,"CommitBatch",new[]{remaining},"NO_POINT",Path.Combine(dir,"不存在目录","session.json"))!);Assert.Same(before,Field<CoverageReport>(form,"report"));
                        Field<CheckBox>(form,"selectAllIssues").Checked=true;for(int row=0;row<issues.Rows.Count;row++)Assert.Equal((bool)Call(form,"CanSelectIssue",row)!,issues.Rows[row].Cells["选择"].Value);Screenshot(form,"v5-异常批量确认");
                        Call(form,"ShowPage","规则总览");var overview=Field<DataGridView>(form,"ruleList");Assert.False(overview.Columns.Contains("项目"));Assert.False(overview.Columns.Contains("历史依据"));Assert.False(overview.Columns.Contains("来源"));Assert.Equal(326,overview.Rows.Count);Assert.All(cfg.Rules,r=>Assert.False(string.IsNullOrWhiteSpace(r.Source)));Screenshot(form,"v5-规则总览");
                        Call(form,"ShowPage","规则设置");Assert.False(Field<DataGridView>(form,"rulesGrid").Columns.Contains("检测项目"));Screenshot(form,"v5-规则设置");
                        var editorType=typeof(MainForm).Assembly.GetType("ICTCoverage.WinForms.RuleEditorForm")!;using(var editor=(Form)Activator.CreateInstance(editorType,cfg,null)!){editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-16000,-16000);editor.Show(form);Application.DoEvents();Assert.DoesNotContain(Children(editor),c=>c.Text=="适用检测项目");Assert.DoesNotContain(Children(editor),c=>c is CheckedListBox);Field<ComboBox>(editor,"type").SelectedItem="电阻";Field<TextBox>(editor,"prefixes").Text="ZZ";Field<ComboBox>(editor,"reason").Text=cfg.ReasonText("NO_POINT");Screenshot(editor,"v5-简洁规则表单");Children(editor).OfType<Button>().Single(b=>b.Text=="保存并应用").PerformClick();Assert.Equal(DialogResult.OK,editor.DialogResult);var rule=(ClassificationRule)editorType.GetProperty("Rule")!.GetValue(editor)!;Assert.Equal(4,rule.Inspections.Length);Assert.Equal("ZZ",Assert.Single(rule.Prefixes));}
                        var old=cfg.Rules.Single(r=>r.RuleId=="C-05");using(var editor=(Form)Activator.CreateInstance(editorType,cfg,old)!){editor.Show(form);Application.DoEvents();Children(editor).OfType<Button>().Single(b=>b.Text=="保存并应用").PerformClick();Assert.Equal(DialogResult.OK,editor.DialogResult);var rule=(ClassificationRule)editorType.GetProperty("Rule")!.GetValue(editor)!;Assert.Equal(old.Inspections,rule.Inspections);Assert.Equal(old.ExcludeSuffixes,rule.ExcludeSuffixes);}
                        var next=ReasonManagementService.Upsert(cfg,"UI-CAUSE","界面原因同步","WHAPPY","界面回归");next=ReasonManagementService.Upsert(next,"NO_POINT",cfg.ReasonText("NO_POINT")+"（更新）","WHAPPY","缓存失效回归");await (Task)Call(form,"ApplyRules",next)!;Assert.Contains(Field<ComboBox>(form,"batchReason").Items.Cast<object>(),r=>r.ToString()=="界面原因同步");Call(form,"ShowPage","人工修改日志");Assert.Equal(2,Field<DataGridView>(form,"log").Rows.Count);Assert.Contains(Enumerable.Range(0,grid.RowCount),r=>grid.GetValue(r,"盲点原因")!.ToString()!.Contains("（更新）"));Call(form,"ShowPage","盲点设置");Screenshot(form,"v5-盲点设置");Call(form,"ShowPage","类型分类设置");Assert.True(Field<DataGridView>(form,"typeGrid").Rows.Count>0);Screenshot(form,"v5-类型分类设置");var typed=TypeManagementService.Upsert(Field<RuleConfig>(form,"cfg"),null,"界面新增分类",["ZZA"],"WHAPPY","界面同步");await(Task)Call(form,"ApplyRules",typed)!;Assert.Contains("界面新增分类",type.Items.Cast<string>());typed=TypeManagementService.Archive(typed,"界面新增分类",true,"WHAPPY","删除同步");await(Task)Call(form,"ApplyRules",typed)!;Assert.DoesNotContain("界面新增分类",type.Items.Cast<string>());
                    }catch(Exception ex){failure=ex;}finally{form.Close();}
                };Application.Run(form);
            }catch(Exception ex){failure=ex;}finally{completion.TrySetResult();}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
