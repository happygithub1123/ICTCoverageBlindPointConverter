using Xunit;
using ICTCoverage.WinForms;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;

namespace ICTCoverage.Tests;

public sealed class UiTests
{
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
                        await (Task)typeof(MainForm).GetMethod("ReadAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null)!;
                        var type=(ComboBox)typeof(MainForm).GetField("type",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;type.SelectedItem="电阻";
                        var filter=(TextBox)typeof(MainForm).GetField("valueFilter",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;filter.Text="0.Ω";
                        var grid=(DataGridView)typeof(MainForm).GetField("grid",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
                        Assert.Single(grid.Rows.Cast<DataGridViewRow>());Assert.Equal(165,grid.Rows[0].Cells["盲点数量"].Value);
                        var button=(Button)typeof(MainForm).GetField("copyFour",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;Assert.True(button.Enabled);button.PerformClick();Assert.True(Clipboard.ContainsData(DataFormats.Html));Assert.Contains("R103.R104.",Clipboard.GetText());
                        using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(ConverterTests.Root,"outputs","界面预览.png"));
                    }
                    catch(Exception ex){error=ex;}
                    finally{form.Close();}
                };
                Application.Run(form);
            }catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(45)),"界面测试超时");if(error!=null)throw error;
    }
}
