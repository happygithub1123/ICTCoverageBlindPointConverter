using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using ICTCoverage.WinForms;
using Xunit;
namespace ICTCoverage.Tests;

[Collection("WinForms")]
public sealed class NavigationTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object form,string name)=>(T)form.GetType().GetField(name,Private)!.GetValue(form)!;
    static IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    [Theory]
    [InlineData(1120,760,1f)] [InlineData(1440,960,1f)]
    [InlineData(1120,760,1.25f)] [InlineData(1440,960,1.25f)]
    [InlineData(1120,760,1.5f)] [InlineData(1440,960,1.5f)]
    public async Task MenuLabelsHelpAndScaledLayout(int width,int height,float scale)
    {
        Exception? failure=null;var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try
            {
                Application.EnableVisualStyles();using var form=new MainForm(Path.Combine(ConverterTests.Root,"outputs","nav-"+Guid.NewGuid().ToString("N")));
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Size=new Size(width,height);form.Show();Application.DoEvents();
                // Exercise proportional layout without changing the user's Windows display setting.
                if(scale!=1)
                {
                    var fonts=Children(form).Prepend(form).Select(c=>(Control:c,Family:c.Font.FontFamily,Size:c.Font.Size,Style:c.Font.Style)).ToArray();
                    form.AutoScaleMode=AutoScaleMode.None;form.Scale(new SizeF(scale,scale));
                    foreach(var item in fonts)item.Control.Font=new Font(item.Family,item.Size*scale,item.Style);
                    form.Size=new Size((int)(width*scale),(int)(height*scale));form.PerformLayout();foreach(var child in Children(form))child.PerformLayout();Application.DoEvents();
                }
                var credit=Children(form).Single(c=>c.Name=="DesignerCredit");Assert.True(form.RectangleToScreen(form.ClientRectangle).Contains(credit.RectangleToScreen(credit.ClientRectangle)),$"署名必须完整位于窗口内 window={form.ClientRectangle} credit={form.RectangleToClient(credit.RectangleToScreen(credit.ClientRectangle))} min={form.MinimumSize} root={form.Controls[0].Bounds} outer={credit.Parent!.Bounds} body={credit.Parent.Parent!.Bounds} rowstyles={string.Join(",",((TableLayoutPanel)credit.Parent).RowStyles.Cast<RowStyle>().Select(r=>r.Height))}");
                var nav=Field<Dictionary<string,Button>>(form,"nav");string[] labels=["分类筛选","复制整理","待确认项","盲点设置","分类设置","规则设置","规则总览","修改记录","使用说明"];
                Assert.Equal(labels,nav.Values.Select(b=>b.Text));
                var menu=Children(form).OfType<TableLayoutPanel>().Single(c=>c.Name=="NavigationMenu");Assert.Equal(menu.RowCount,menu.RowStyles.Count);Assert.Equal(12,menu.RowCount);
                var ordered=nav.Values.ToArray();for(int i=1;i<ordered.Length;i++){Assert.True(ordered[i].Top>=ordered[i-1].Bottom);Assert.InRange(ordered[i].Top-ordered[i-1].Bottom,0,25*scale);}
                var host=Children(form).OfType<Panel>().Single(c=>c.Name=="NavigationScroll");
                foreach(var pair in nav){host.ScrollControlIntoView(pair.Value);pair.Value.PerformClick();Application.DoEvents();Assert.Equal(pair.Value.Text,Field<Label>(form,"title").Text);Assert.True(Field<Dictionary<string,Control>>(form,"pages")[pair.Key].Visible);}
                Assert.True(host.RectangleToScreen(host.ClientRectangle).Contains(nav["使用说明"].RectangleToScreen(nav["使用说明"].ClientRectangle)));
                var badge=Field<Label>(form,"pendingBadge");var original=nav["异常待确认"].Size;
                typeof(MainForm).GetMethod("UpdatePendingBadge",Private)!.Invoke(form,[123456]);Assert.Equal("9999+",badge.Text);Assert.Equal(original,nav["异常待确认"].Size);Assert.Equal("待确认项",nav["异常待确认"].Text);
                var topics=Children(form).OfType<ComboBox>().Single(c=>c.Name=="HelpTopics");var body=Children(form).OfType<RichTextBox>().Single(c=>c.Name=="HelpBody");Assert.True(body.ReadOnly);Assert.Contains("R103.R104.",body.Text);
                foreach(string label in labels){Assert.Contains(label,topics.Items.Cast<string>());topics.SelectedItem=label;Assert.StartsWith(label,body.Text);Assert.True(body.Text.Length>40);}
                topics.SelectedIndex=0;Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(ConverterTests.Root,"outputs",$"v054-help-{width}-{scale:0.00}.png"));
                nav["分类筛选"].PerformClick();host.AutoScrollPosition=Point.Empty;Application.DoEvents();form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(ConverterTests.Root,"outputs",$"v054-nav-{width}-{scale:0.00}.png"));
                Assert.DoesNotContain(Children(form),c=>c.Text.Contains("陈俊杰工程师"));Assert.True(Field<Button>(form,"allInspection").Visible);Assert.True(Field<Label>(form,"sidebarStats").Visible);form.Close();
            }
            catch(Exception ex){failure=ex;}finally{done.TrySetResult();}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(45));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
