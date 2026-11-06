using Xunit;
using ICTCoverage.WinForms;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using ICTCoverage.Core;

namespace ICTCoverage.Tests;

public sealed class UiTests
{
    static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
    [Fact] public void MainForm_LoadFilterAndClipboard_Smoke()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();
                using var form=new MainForm(Path.Combine(ConverterTests.Root,"outputs","ui-test-state-"+Guid.NewGuid().ToString("N")));
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);
                form.Shown+=async(_,_)=>
                {
                    try
                    {
                        ((TextBox)typeof(MainForm).GetField("input",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!).Text=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();
                        await (Task)typeof(MainForm).GetMethod("ReadAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null)!;
                        var type=(ComboBox)typeof(MainForm).GetField("type",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;type.SelectedItem="电阻";
                        var filter=(TextBox)typeof(MainForm).GetField("valueFilter",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;filter.Text="0.Ω";
                        var grid=(DataGridView)typeof(MainForm).GetField("grid",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
                        Assert.Single(grid.Rows.Cast<DataGridViewRow>());Assert.Equal(165,grid.Rows[0].Cells["盲点数量"].Value);
                        var button=(Button)typeof(MainForm).GetField("copyFour",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.True(button.Enabled);button.PerformClick();Assert.True(Clipboard.ContainsData(DataFormats.Html));Assert.Contains("R103.R104.",Clipboard.GetText());
                        var allControls=Descendants(form).ToList();Assert.DoesNotContain(allControls,c=>c.Text=="选择模板"||c.Text=="核对报告"||c.Text=="复制原始明细"||c.Text=="修改选中明细");Assert.Contains(allControls,c=>c.Text.Contains("WANG KAIXIN"));Assert.NotNull(form.Icon);
                        var outDir=Path.Combine(ConverterTests.Root,"outputs","ui-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outDir);
                        await (Task)typeof(MainForm).GetMethod("WriteBlindPointAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[Path.Combine(outDir,"盲点表.xlsx"),new ICTCoverage.Core.ExportOptions("","材号")])!;Assert.Single(Directory.GetFiles(outDir));
                        var loaded=(ICTCoverage.Core.Analysis)typeof(MainForm).GetField("analysis",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;string originalPath=loaded.SourcePath;
                        string lockedFile=Path.Combine(outDir,"editor-open.xls");File.Copy(originalPath,lockedFile);loaded.SourcePath=lockedFile;
                        try{using var editorLock=new FileStream(lockedFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None);var session=(string)typeof(MainForm).GetMethod("SessionPath",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null)!;Assert.Contains(loaded.SourceFingerprint,session);}
                        finally{loaded.SourcePath=originalPath;File.Delete(lockedFile);}Assert.Single(Directory.GetFiles(outDir));
                        filter.Clear();type.SelectedItem="全部";
                        var cards=(System.Collections.IDictionary)typeof(MainForm).GetField("cards",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.Equal(4,cards.Count);
                        var shortCard=(Control)cards[InspectionKind.Short]!;var yes=(LinkLabel)shortCard.GetType().GetField("yes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shortCard)!;
                        typeof(LinkLabel).GetMethod("OnLinkClicked",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(yes,[new LinkLabelLinkClickedEventArgs(yes.Links[0])]);
                        var current=(Analysis)typeof(MainForm).GetField("analysis",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.Equal(InspectionKind.Short,current.Inspection);
                        Assert.Equal(381,grid.Rows.Cast<DataGridViewRow>().Sum(r=>(int)r.Cells["盲点数量"].Value));Assert.False(button.Enabled);
                        type.SelectedItem="电阻";Assert.Equal(67,grid.Rows.Cast<DataGridViewRow>().Sum(r=>(int)r.Cells["盲点数量"].Value));
                        Assert.Equal("12%",((Label)shortCard.GetType().GetField("percent",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shortCard)!).Text);
                        var side=(Label)typeof(MainForm).GetField("sidebarStats",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.Contains("821",side.Text);Assert.Contains("31.69%",side.Text);
                        var item=current.Components.First(c=>c.State==TestState.Untestable);var testCfg=RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));
                        AnalysisEngine.Edit(current,item,item.Reference,item.Type,item.Value,item.SourceState,"NO_POINT","","界面原因选择",testCfg);
                        typeof(MainForm).GetMethod("RefreshLog",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
                        var finalLog=(DataGridView)typeof(MainForm).GetField("log",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.Single(finalLog.Rows.Cast<DataGridViewRow>());Assert.Equal(5,finalLog.Columns.Count);Assert.Equal("短路",finalLog.Rows[0].Cells["检测项目"].Value);
                        type.SelectedItem="全部";typeof(MainForm).GetMethod("ChooseInspection",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[InspectionKind.Missing,TestState.Untestable]);
                        Assert.DoesNotContain(allControls.OfType<ComboBox>(),c=>c.Items.Cast<object>().Any(x=>x.ToString()=="通用模式"));
                        using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览.png"));
                        form.Size=new Size(1120,760);Application.DoEvents();using var small=new Bitmap(form.Width,form.Height);form.DrawToBitmap(small,new Rectangle(Point.Empty,small.Size));small.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-小窗口.png"));
                        typeof(MainForm).GetMethod("ShowPage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["规则设置"]);Application.DoEvents();using var rules=new Bitmap(form.Width,form.Height);form.DrawToBitmap(rules,new Rectangle(Point.Empty,rules.Size));rules.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-规则设置.png"));
                        typeof(MainForm).GetMethod("ShowPage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["V2规则总览"]);Application.DoEvents();
                        var overview=(DataGridView)typeof(MainForm).GetField("ruleList",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.True(overview.Columns.Contains("当前执行"));Assert.True(overview.Columns.Contains("历史依据"));Assert.False(overview.Columns.Contains("依据"));
                        using var historyImage=new Bitmap(form.Width,form.Height);form.DrawToBitmap(historyImage,new Rectangle(Point.Empty,historyImage.Size));historyImage.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-规则总览.png"));
                        var cfgField=typeof(MainForm).GetField("cfg",BindingFlags.NonPublic|BindingFlags.Instance)!;var apply=typeof(MainForm).GetMethod("ApplyRules",BindingFlags.NonPublic|BindingFlags.Instance)!;
                        typeof(MainForm).GetMethod("ShowPage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["盲点设置"]);Application.DoEvents();
                        var catalog=ReasonManagementService.Upsert((RuleConfig)cfgField.GetValue(form)!,"UI-REASON","界面新增原因","WHAPPY","界面验证");await (Task)apply.Invoke(form,[catalog])!;
                        var search=(TextBox)typeof(MainForm).GetField("reasonSearch",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;search.Text="界面新增原因";
                        var catalogGrid=(DataGridView)typeof(MainForm).GetField("reasonGrid",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.Single(catalogGrid.Rows.Cast<DataGridViewRow>());
                        catalog=ReasonManagementService.Upsert((RuleConfig)cfgField.GetValue(form)!,"UI-REASON","界面修改原因","WHAPPY","名称同步");search.Clear();await (Task)apply.Invoke(form,[catalog])!;search.Text="界面修改原因";Assert.Single(catalogGrid.Rows.Cast<DataGridViewRow>());
                        var causeType=typeof(MainForm).Assembly.GetType("ICTCoverage.WinForms.CauseForm")!;using(var cause=(Form)Activator.CreateInstance(causeType,item,cfgField.GetValue(form),InspectionKind.Short)!){var choices=(ComboBox)causeType.GetField("reason",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(cause)!;Assert.Contains("界面修改原因",choices.Items.Cast<string>());}
                        catalogGrid.CurrentCell=catalogGrid.Rows[0].Cells["盲点原因"];await (Task)typeof(MainForm).GetMethod("ArchiveBlindReason",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[true])!;Assert.Empty(catalogGrid.Rows.Cast<DataGridViewRow>());
                        using(var cause=(Form)Activator.CreateInstance(causeType,item,cfgField.GetValue(form),InspectionKind.Short)!){var choices=(ComboBox)causeType.GetField("reason",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(cause)!;Assert.DoesNotContain("界面修改原因",choices.Items.Cast<string>());}
                        ((CheckBox)typeof(MainForm).GetField("deletedReasons",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!).Checked=true;Assert.Single(catalogGrid.Rows.Cast<DataGridViewRow>());await (Task)typeof(MainForm).GetMethod("ArchiveBlindReason",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[false])!;
                        search.Clear();var catalogHistory=(DataGridView)typeof(MainForm).GetField("reasonHistory",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.Equal(4,catalogHistory.Rows.Count);Assert.Single(finalLog.Rows.Cast<DataGridViewRow>());
                        typeof(MainForm).GetMethod("ShowPage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["盲点设置"]);Application.DoEvents();using var catalogImage=new Bitmap(form.Width,form.Height);form.DrawToBitmap(catalogImage,new Rectangle(Point.Empty,catalogImage.Size));catalogImage.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-盲点设置.png"));
                        var reasonTabs=(TabControl)typeof(MainForm).GetField("reasonTabs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;reasonTabs.SelectedIndex=1;Application.DoEvents();using var reasonLogImage=new Bitmap(form.Width,form.Height);form.DrawToBitmap(reasonLogImage,new Rectangle(Point.Empty,reasonLogImage.Size));reasonLogImage.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-原因操作记录.png"));
                        var cfg=ICTCoverage.Core.RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));var editorType=typeof(MainForm).Assembly.GetType("ICTCoverage.WinForms.RuleEditorForm")!;using var editor=(Form)Activator.CreateInstance(editorType,cfg,null)!;editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-16000,-16000);editor.Show(form);Application.DoEvents();
                        ((ComboBox)editorType.GetField("type",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).SelectedItem="电阻";
                        ((ComboBox)editorType.GetField("reason",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Text="无测试点，故不可测";
                        ((TextBox)editorType.GetField("prefixes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Text="ZZ";
                        ((TextBox)editorType.GetField("why",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Text="界面测试新增规则";
                        var scopes=(CheckedListBox)editorType.GetField("inspectionScopes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!;scopes.SetItemChecked(0,false);scopes.SetItemChecked(1,true);
                        var save=Descendants(editor).OfType<Button>().Single(b=>b.Text=="保存并应用");save.PerformClick();Assert.Equal(DialogResult.OK,editor.DialogResult);var editedRule=(ICTCoverage.Core.ClassificationRule)editorType.GetProperty("Rule")!.GetValue(editor)!;Assert.Equal("ZZ",Assert.Single(editedRule.Prefixes));Assert.Equal(InspectionKind.Short,Assert.Single(editedRule.Inspections));
                    }
                    catch(Exception ex){error=ex;}
                    finally{form.Close();}
                };
                Application.Run(form);
            }catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(45)),"界面测试超时");if(error!=null)throw error;
    }
}
