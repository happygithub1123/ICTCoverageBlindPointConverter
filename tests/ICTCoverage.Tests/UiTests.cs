using Xunit;
using ICTCoverage.WinForms;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;

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
                        using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览.png"));
                        form.Size=new Size(1120,760);Application.DoEvents();using var small=new Bitmap(form.Width,form.Height);form.DrawToBitmap(small,new Rectangle(Point.Empty,small.Size));small.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-小窗口.png"));
                        typeof(MainForm).GetMethod("ShowPage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["规则设置"]);Application.DoEvents();using var rules=new Bitmap(form.Width,form.Height);form.DrawToBitmap(rules,new Rectangle(Point.Empty,rules.Size));rules.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览-规则设置.png"));
                        var cfg=ICTCoverage.Core.RuleConfig.Load(Path.Combine(ConverterTests.Root,"config","rules.json"));var editorType=typeof(MainForm).Assembly.GetType("ICTCoverage.WinForms.RuleEditorForm")!;using var editor=(Form)Activator.CreateInstance(editorType,cfg,null)!;editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-16000,-16000);editor.Show(form);Application.DoEvents();
                        ((ComboBox)editorType.GetField("type",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).SelectedItem="电阻";
                        ((ComboBox)editorType.GetField("reason",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Text="无测试点，故不可测";
                        ((TextBox)editorType.GetField("prefixes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Text="ZZ";
                        ((TextBox)editorType.GetField("why",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Text="界面测试新增规则";
                        var save=Descendants(editor).OfType<Button>().Single(b=>b.Text=="保存并应用");save.PerformClick();Assert.Equal(DialogResult.OK,editor.DialogResult);var editedRule=(ICTCoverage.Core.ClassificationRule)editorType.GetProperty("Rule")!.GetValue(editor)!;Assert.Equal("ZZ",Assert.Single(editedRule.Prefixes));
                    }
                    catch(Exception ex){error=ex;}
                    finally{form.Close();}
                };
                Application.Run(form);
            }catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(45)),"界面测试超时");if(error!=null)throw error;
    }
}
