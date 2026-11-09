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
        Assert.Empty(user.Text);Assert.Empty(password.Text);Assert.True(password.UseSystemPasswordChar);Assert.Same(button,form.AcceptButton);
        foreach(var pair in new[]{("陈俊杰",""),("","234542"),("错误用户","234542"),("陈俊杰","12345"),("陈俊杰","123456 ")})
        {user.Text=pair.Item1;password.Text=pair.Item2;button.PerformClick();Assert.NotEqual(DialogResult.OK,form.DialogResult);Assert.Equal("用户名或密码错误",error.Text);Assert.True(form.Visible);}
        var show=Field<CheckBox>(form,"showPassword");show.Checked=true;Assert.False(password.UseSystemPasswordChar);show.Checked=false;Assert.True(password.UseSystemPasswordChar);
        user.Text="陈俊杰";password.Text="234542";typeof(Form).GetMethod("ProcessDialogKey",Private)!.Invoke(form,[Keys.Enter]);Assert.Equal("登录成功，正在进入…",error.Text);await Task.Delay(450);Assert.Equal(DialogResult.OK,form.DialogResult);Assert.Empty(password.Text);Assert.False(Property<bool>(form,"AnimationRunning"));
        using var reopened=Form();reopened.Show();Assert.Empty(Field<TextBox>(reopened,"username").Text);Assert.Empty(Field<TextBox>(reopened,"password").Text);reopened.Close();Assert.NotEqual(DialogResult.OK,reopened.DialogResult);await Task.CompletedTask;
    });
    [Fact]public void FourNamesAndSixAsciiDigitsSumTwenty()
    {
        var validate=typeof(LoginForm).Assembly.GetType("ICTCoverage.WinForms.LoginCredentials")!.GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool Check(string name,string code)=>(bool)validate.Invoke(null,[name,code])!;
        foreach(string name in new[]{"君和辰","陈俊杰","WHAPPY","星联达"," WHAPPY "})foreach(string code in new[]{"234542","000299"})Assert.True(Check(name,code));
        foreach(string name in new[]{"","whappy","Whappy","其他用户"})Assert.False(Check(name,"234542"));
        foreach(string code in new[]{"","123456","WHAPPY@1123","23454","2345420","234542 "," 234542","２３４５４２","23454A","000000"})Assert.False(Check("君和辰",code));
    }
    [Fact]public Task LoopKeepsInputsStableAndPausesOnMinimize()=>Sta(async()=>
    {
        using var form=Form();form.Show();await Task.Delay(1000);var card=Field<Panel>(form,"card");var stable=card.Bounds;var user=Field<TextBox>(form,"username");user.Text="WHAPPY";user.Select(2,0);user.Focus();
        using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));int builds=Property<int>(form,"BackgroundBuilds");
        var process=System.Diagnostics.Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;long memory=process.PrivateMemorySize64;var watch=System.Diagnostics.Stopwatch.StartNew();float phase=Property<float>(form,"LoopPhase");
        for(int i=0;i<70;i++){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));await Task.Delay(100);Assert.Equal(stable,card.Bounds);Assert.Equal("WHAPPY",user.Text);Assert.Equal(2,user.SelectionStart);}
        process.Refresh();File.WriteAllText(Path.Combine(ConverterTests.Root,"outputs","login-loop-v055.json"),System.Text.Json.JsonSerializer.Serialize(new{elapsedMs=watch.ElapsedMilliseconds,cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,memoryDelta=process.PrivateMemorySize64-memory,backgroundBuilds=Property<int>(form,"BackgroundBuilds")-builds},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Assert.Equal(builds,Property<int>(form,"BackgroundBuilds"));Assert.True(Property<bool>(form,"AnimationRunning"));Assert.NotEqual(phase,Property<float>(form,"LoopPhase"));
        form.WindowState=FormWindowState.Minimized;await Task.Delay(100);Assert.False(Property<bool>(form,"AnimationRunning"));form.WindowState=FormWindowState.Normal;await Task.Delay(100);Assert.True(Property<bool>(form,"AnimationRunning"));form.Close();Assert.False(Property<bool>(form,"AnimationRunning"));
    });
    [Theory]
    [InlineData(900,600)] [InlineData(1200,760)] [InlineData(1500,950)] [InlineData(1800,1140)]
    public Task LayoutAnimationAndBackgroundCache(int width,int height)=>Sta(async()=>
    {
        using var form=Form(width,height);form.Show();await Task.Delay(1100);Assert.True(Property<bool>(form,"AnimationRunning"));Assert.InRange(Field<float>(form,"progress"),1,1);
        var card=Field<Panel>(form,"card");Assert.True(form.ClientRectangle.Contains(card.Bounds));foreach(Control child in card.Controls)Assert.True(card.ClientRectangle.Contains(child.Bounds),child.GetType().Name);
        var company=Field<RectangleF>(form,"companyBounds");Assert.True(form.ClientRectangle.Contains(Rectangle.Ceiling(company)));
        var bounds=Field<RectangleF>(form,"englishBounds");float scale=Field<float>(form,"layoutScale");using var probe=new Bitmap(1,1);using var graphics=Graphics.FromImage(probe);using var titleFont=new Font("Segoe UI",32*scale,FontStyle.Bold,GraphicsUnit.Pixel);
        Assert.True(graphics.MeasureString(LoginForm.CompanyTitle,Field<Font>(form,"companyFont")).Width<=company.Width);
        foreach(string line in LoginForm.EnglishTitle.Split('\n'))Assert.True(graphics.MeasureString(line,titleFont).Width<=bounds.Width);
        Assert.True(graphics.MeasureString(LoginForm.EnglishTitle,titleFont,(int)bounds.Width).Height<=bounds.Height);
        using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));int builds=Property<int>(form,"BackgroundBuilds");for(int i=0;i<3;i++){form.Invalidate();form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));}Assert.Equal(builds,Property<int>(form,"BackgroundBuilds"));
        bitmap.Save(Path.Combine(ConverterTests.Root,"outputs",$"v055-login-{width}.png"));
        form.Size=new Size(width-20,height-20);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));Assert.True(Property<int>(form,"BackgroundBuilds")>builds);Assert.True(Property<bool>(form,"AnimationRunning"));form.Close();Assert.False(Property<bool>(form,"AnimationRunning"));
    });
}
