using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using ICTCoverage.WinForms;
using Xunit;
namespace ICTCoverage.Tests;

[Collection("WinForms")]
public sealed class LoginTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private)!.GetValue(target)!;
    static T Property<T>(object target,string name)=>(T)target.GetType().GetProperty(name,Private)!.GetValue(target)!;
    static async Task Sta(Func<Task> work)
    {
        Exception? failure=null;var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();using var context=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Size=new Size(1,1)};context.Shown+=async(_,_)=>{try{await work();}catch(Exception ex){failure=ex;}finally{context.Close();}};Application.Run(context);}catch(Exception ex){failure=ex;}finally{done.TrySetResult();}});
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(30));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    static LoginForm Form(int width=1200,int height=760)=>new(){StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Size=new Size(width,height)};
    [Fact]public Task CredentialsMaskingAndRestartRequirePassword()=>Sta(async()=>
    {
        using var form=Form();form.Show();var user=Field<TextBox>(form,"username");var password=Field<TextBox>(form,"password");var button=Field<Button>(form,"login");var error=Field<Label>(form,"error");
        Assert.Equal("陈俊杰",user.Text);Assert.Empty(password.Text);Assert.True(password.UseSystemPasswordChar);Assert.Same(button,form.AcceptButton);
        foreach(var pair in new[]{("陈俊杰",""),("","123456"),("错误用户","123456"),("陈俊杰","12345"),("陈俊杰","123456 ")})
        {user.Text=pair.Item1;password.Text=pair.Item2;button.PerformClick();Assert.NotEqual(DialogResult.OK,form.DialogResult);Assert.Equal("用户名或密码错误",error.Text);Assert.True(form.Visible);}
        var show=Field<CheckBox>(form,"showPassword");show.Checked=true;Assert.False(password.UseSystemPasswordChar);show.Checked=false;Assert.True(password.UseSystemPasswordChar);
        user.Text="陈俊杰";password.Text="123456";typeof(Form).GetMethod("ProcessDialogKey",Private)!.Invoke(form,[Keys.Enter]);Assert.Equal(DialogResult.OK,form.DialogResult);Assert.Empty(password.Text);Assert.False(Property<bool>(form,"AnimationRunning"));
        using var reopened=Form();reopened.Show();Assert.Empty(Field<TextBox>(reopened,"password").Text);reopened.Close();Assert.NotEqual(DialogResult.OK,reopened.DialogResult);await Task.CompletedTask;
    });
    [Theory]
    [InlineData(900,600)] [InlineData(1200,760)] [InlineData(1500,950)] [InlineData(1800,1140)]
    public Task LayoutAnimationAndBackgroundCache(int width,int height)=>Sta(async()=>
    {
        using var form=Form(width,height);form.Show();await Task.Delay(1100);Assert.False(Property<bool>(form,"AnimationRunning"));Assert.InRange(Field<float>(form,"progress"),1,1);
        var card=Field<Panel>(form,"card");Assert.True(form.ClientRectangle.Contains(card.Bounds));foreach(Control child in card.Controls)Assert.True(card.ClientRectangle.Contains(child.Bounds),child.GetType().Name);
        var bounds=Field<RectangleF>(form,"englishBounds");float scale=Field<float>(form,"layoutScale");using var probe=new Bitmap(1,1);using var graphics=Graphics.FromImage(probe);using var titleFont=new Font("Segoe UI",32*scale,FontStyle.Bold,GraphicsUnit.Pixel);
        foreach(string line in LoginForm.EnglishTitle.Split('\n'))Assert.True(graphics.MeasureString(line,titleFont).Width<=bounds.Width);
        Assert.True(graphics.MeasureString(LoginForm.EnglishTitle,titleFont,(int)bounds.Width).Height<=bounds.Height);
        using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));int builds=Property<int>(form,"BackgroundBuilds");for(int i=0;i<3;i++){form.Invalidate();form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));}Assert.Equal(builds,Property<int>(form,"BackgroundBuilds"));
        bitmap.Save(Path.Combine(ConverterTests.Root,"outputs",$"v054-login-{width}.png"));
        form.Size=new Size(width-20,height-20);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));Assert.True(Property<int>(form,"BackgroundBuilds")>builds);Assert.False(Property<bool>(form,"AnimationRunning"));form.Close();
    });
}
