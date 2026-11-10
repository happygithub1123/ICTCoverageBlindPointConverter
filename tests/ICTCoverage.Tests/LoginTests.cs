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
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(120));thread.Join();if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    static LoginForm Form(int width=1200,int height=760)=>new(){ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Size=new Size(width,height)};
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetGuiResources(IntPtr process,uint kind);
    [Fact]public Task CompanyHighlightActuallyTravelsWithTimer()=>Sta(async()=>
    {
        using var form=Form();form.Show();await Task.Delay(900);
        var company=Field<Control>(form,"companyTitle");var title=Field<Control>(form,"englishTitle");
        float initial=Property<float>(company,"Phase");await Task.Delay(350);
        Assert.NotEqual(initial,Property<float>(company,"Phase"));
        Assert.InRange(Math.Abs(Property<float>(company,"Phase")-Property<float>(title,"Phase")),0,.001f);
        form.Hide();int builds=Property<int>(company,"CacheBuilds");
        double Center(float phase,string name)
        {
            company.GetType().GetProperty("Phase",Private)!.SetValue(company,phase);
            using var image=new Bitmap(company.Width,company.Height);company.DrawToBitmap(image,company.ClientRectangle);
            var layer=Field<Bitmap>(company,"flowOverlay");double weight=0,sum=0;
            for(int y=0;y<layer.Height;y++)for(int x=0;x<layer.Width;x++){int alpha=layer.GetPixel(x,y).A;weight+=alpha;sum+=x*alpha;}
            Assert.True(weight>500,"光带必须实际覆盖公司名称字形");
            image.Save(Path.Combine(ConverterTests.Root,"outputs",$"company-flow-{name}.png"));return sum/weight;
        }
        double left=Center(.25f,"left"),middle=Center(.5f,"middle"),right=Center(.75f,"right");
        Assert.True(middle>left+20&&right>middle+20,$"光带中心必须右移：{left:F1}, {middle:F1}, {right:F1}");
        Assert.Equal(builds,Property<int>(company,"CacheBuilds"));form.Close();
    });
    [Fact]public Task CachedFlowAndHalfTransparentCard()=>Sta(async()=>
    {
        using var form=Form();form.Show();await Task.Delay(1100);
        var card=Field<Panel>(form,"card");Assert.Contains(card.Controls.Cast<Control>(),c=>c is Label&&c.Text=="用户名");Assert.Contains(card.Controls.Cast<Control>(),c=>c is Label&&c.Text=="密码");
        Assert.Empty(Field<TextBox>(form,"username").Text);Assert.Empty(Field<TextBox>(form,"password").Text);
        Assert.Equal(42,Field<System.Windows.Forms.Timer>(form,"entrance").Interval);
        var title=Field<Control>(form,"englishTitle");int builds=Property<int>(title,"CacheBuilds");
        using var capture=new Bitmap(card.Width,card.Height);card.DrawToBitmap(capture,card.ClientRectangle);
        var background=Field<Bitmap>(form,"background");Color source=background.GetPixel(card.Left+10,card.Top+150),actual=capture.GetPixel(10,150);
        Assert.InRange(actual.R,(source.R+250)/2-2,(source.R+250)/2+2);Assert.InRange(actual.G,(source.G+252)/2-2,(source.G+252)/2+2);Assert.InRange(actual.B,(source.B+255)/2-2,(source.B+255)/2+2);
        form.Hide();float paused=Property<float>(form,"LoopPhase");await Task.Delay(150);Assert.False(Property<bool>(form,"AnimationRunning"));Assert.Equal(paused,Property<float>(form,"LoopPhase"));
        using var first=new Bitmap(title.Width,title.Height);using var second=new Bitmap(title.Width,title.Height);
        title.GetType().GetProperty("Phase",Private)!.SetValue(title,.25f);title.DrawToBitmap(first,title.ClientRectangle);
        title.GetType().GetProperty("Phase",Private)!.SetValue(title,.70f);title.DrawToBitmap(second,title.ClientRectangle);
        int changed=0;for(int y=0;y<title.Height;y++)for(int x=0;x<title.Width;x++)if(first.GetPixel(x,y)!=second.GetPixel(x,y))changed++;
        Assert.True(changed>200);Assert.Equal(builds,Property<int>(title,"CacheBuilds"));
        form.Show();await Task.Delay(150);Assert.True(Property<bool>(form,"AnimationRunning"));form.Close();Assert.True(title.IsDisposed);
    });
    [Fact]public Task SixtySecondAnimationInputProfile()=>Sta(async()=>
    {
        using var form=Form();form.Show();await Task.Delay(1500);
        using(var warm=new Bitmap(form.Width,form.Height))form.DrawToBitmap(warm,new Rectangle(Point.Empty,warm.Size));
        var user=Field<TextBox>(form,"username");var password=Field<TextBox>(form,"password");var show=Field<CheckBox>(form,"showPassword");var card=Field<Panel>(form,"card");var stable=card.Bounds;
        var hooks=new List<PaintProbe>();void Hook(Control c){hooks.Add(new PaintProbe(c));foreach(Control child in c.Controls)Hook(child);}Hook(form);
        // Offscreen Windows may discard WM_PAINT. Replay only invalidated regions
        // into cached canvases so baseline and fix execute actual paint work
        // without putting test credentials on the user's desktop.
        using var replay=new InvalidationReplay(form);
        int parentPaints=0;form.Paint+=(_,_)=>parentPaints++;
        var process=System.Diagnostics.Process.GetCurrentProcess();process.Refresh();var cpu=process.TotalProcessorTime;long memory=process.PrivateMemorySize64;int handles=process.HandleCount;
        int builds=Property<int>(form,"BackgroundBuilds");var elapsed=System.Diagnostics.Stopwatch.StartNew();var gaps=new List<double>();double previous=0;
        int cycles=0;var resources=new List<object>();var gdiSamples=new List<uint>();var userSamples=new List<uint>();int nextSample=15;
        while(elapsed.Elapsed.TotalSeconds<60)
        {
            user.Focus();user.Text="WHAPPY";user.Select(2,0);
            typeof(Form).GetMethod("ProcessDialogKey",Private)!.Invoke(form,[Keys.Tab]);Assert.True(password.Focused);
            password.Text="000299";show.Checked=cycles%2==0;Assert.Equal(!show.Checked,password.UseSystemPasswordChar);
            await Task.Delay(250);
            Assert.Equal("WHAPPY",user.Text);Assert.Equal("000299",password.Text);Assert.Equal(stable,card.Bounds);
            double now=elapsed.Elapsed.TotalMilliseconds;gaps.Add(now-previous);previous=now;cycles++;
            if(now>=nextSample*1000)
            {
                process.Refresh();uint gdi=GetGuiResources(process.Handle,0),users=GetGuiResources(process.Handle,1);gdiSamples.Add(gdi);userSamples.Add(users);
                resources.Add(new{seconds=now/1000,gdi,users,privateBytes=process.PrivateMemorySize64,handles=process.HandleCount});nextSample+=15;
            }
        }
        process.Refresh();gaps.Sort();var results=new{elapsedMs=elapsed.ElapsedMilliseconds,cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,memoryDelta=process.PrivateMemorySize64-memory,handleDelta=process.HandleCount-handles,parentPaints,paintCalls=hooks.Sum(h=>h.Calls),paintMs=hooks.Sum(h=>h.Milliseconds),maxPaintMs=hooks.Max(h=>h.MaxMilliseconds),inputCycles=cycles,maxResponseIntervalMs=gaps.Max(),p95ResponseIntervalMs=gaps[(int)(gaps.Count*.95)],backgroundBuilds=Property<int>(form,"BackgroundBuilds")-builds};
        string label=Environment.GetEnvironmentVariable("ICT_LOGIN_PROFILE")??"local-fix";
        File.WriteAllText(Path.Combine(ConverterTests.Root,"outputs",$"login-profile-{label}.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(ConverterTests.Root,"outputs",$"login-paint-{label}.json"),System.Text.Json.JsonSerializer.Serialize(new{mode="offscreen invalidation replay; not desktop frame rate",replay.Calls,replay.TotalMs,replay.MaxMs},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(ConverterTests.Root,"outputs",$"login-resources-{label}.json"),System.Text.Json.JsonSerializer.Serialize(resources,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Assert.True(gdiSamples.Max()-gdiSamples.Min()<=5);Assert.True(userSamples.Max()-userSamples.Min()<=3);
        foreach(var hook in hooks)hook.Dispose();Assert.Equal(builds,Property<int>(form,"BackgroundBuilds"));
        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(ConverterTests.Root,"outputs",$"login-profile-{label}.png"));}
        form.WindowState=FormWindowState.Minimized;await Task.Delay(150);Assert.False(Property<bool>(form,"AnimationRunning"));form.WindowState=FormWindowState.Normal;await Task.Delay(150);Assert.True(Property<bool>(form,"AnimationRunning"));
        typeof(Form).GetMethod("ProcessDialogKey",Private)!.Invoke(form,[Keys.Enter]);await Task.Delay(450);Assert.Equal(DialogResult.OK,form.DialogResult);Assert.False(Property<bool>(form,"AnimationRunning"));
    });
    sealed class PaintProbe : NativeWindow,IDisposable
    {
        public int Calls;public double Milliseconds,MaxMilliseconds;
        public PaintProbe(Control control)=>AssignHandle(control.Handle);
        protected override void WndProc(ref Message message)
        {
            if(message.Msg!=0x000F){base.WndProc(ref message);return;}
            long start=System.Diagnostics.Stopwatch.GetTimestamp();base.WndProc(ref message);
            double ms=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;Calls++;Milliseconds+=ms;MaxMilliseconds=Math.Max(MaxMilliseconds,ms);
        }
        public void Dispose()=>ReleaseHandle();
    }
    sealed class InvalidationReplay : IDisposable
    {
        readonly List<(Control Control,Bitmap Canvas,InvalidateEventHandler Handler)> controls=[];
        readonly Dictionary<Control,Rectangle> pending=[];
        readonly System.Windows.Forms.Timer timer=new(){Interval=16};
        static readonly MethodInfo Background=typeof(Control).GetMethod("OnPaintBackground",Private)!,Paint=typeof(Control).GetMethod("OnPaint",Private)!;
        public int Calls;public double TotalMs,MaxMs;
        public InvalidationReplay(Control root)
        {
            void Track(Control control)
            {
                if(control is System.Windows.Forms.Form or Panel||control.GetType().Assembly==typeof(LoginForm).Assembly)
                {
                    var canvas=new Bitmap(Math.Max(1,control.Width),Math.Max(1,control.Height));
                    InvalidateEventHandler handler=(_,e)=>pending[control]=pending.TryGetValue(control,out var old)?Rectangle.Union(old,e.InvalidRect):e.InvalidRect;
                    control.Invalidated+=handler;controls.Add((control,canvas,handler));
                }
                foreach(Control child in control.Controls)Track(child);
            }
            Track(root);timer.Tick+=(_,_)=>Flush();timer.Start();
        }
        void Flush()
        {
            foreach(var item in controls)
            {
                if(!pending.Remove(item.Control,out var rect)||item.Control.IsDisposed||rect.IsEmpty)continue;
                long start=System.Diagnostics.Stopwatch.GetTimestamp();using var g=Graphics.FromImage(item.Canvas);g.SetClip(rect);
                using var args=new PaintEventArgs(g,rect);Background.Invoke(item.Control,[args]);Paint.Invoke(item.Control,[args]);
                double ms=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;Calls++;TotalMs+=ms;MaxMs=Math.Max(MaxMs,ms);
            }
        }
        public void Dispose(){timer.Dispose();foreach(var item in controls){item.Control.Invalidated-=item.Handler;item.Canvas.Dispose();}}
    }
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
        Assert.Equal(builds,Property<int>(form,"BackgroundBuilds"));Assert.True(Property<bool>(form,"AnimationRunning"),$"Visible={form.Visible}, State={form.WindowState}, Closed={Field<bool>(form,"closed")}, LoggingIn={Field<bool>(form,"loggingIn")}");Assert.NotEqual(phase,Property<float>(form,"LoopPhase"));
        form.WindowState=FormWindowState.Minimized;await Task.Delay(100);Assert.False(Property<bool>(form,"AnimationRunning"));form.WindowState=FormWindowState.Normal;await Task.Delay(100);Assert.True(Property<bool>(form,"AnimationRunning"));form.Close();Assert.False(Property<bool>(form,"AnimationRunning"));
    });
    [Theory]
    [InlineData(800,560)] [InlineData(900,600)] [InlineData(1200,760)] [InlineData(1500,950)] [InlineData(1800,1140)]
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
        form.Size=new Size(width==800?width+20:width-20,height==560?height+20:height-20);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));Assert.True(Property<int>(form,"BackgroundBuilds")>builds);Assert.True(Property<bool>(form,"AnimationRunning"));form.Close();Assert.False(Property<bool>(form,"AnimationRunning"));
    });
}
