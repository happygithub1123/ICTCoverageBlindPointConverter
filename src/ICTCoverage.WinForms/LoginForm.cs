using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ICTCoverage.WinForms;

internal static class LoginCredentials
{
    // Local startup gate; never persist the entered password in rules, sessions or logs.
    internal static bool Validate(string user,string password)
    {
        string name=user.Trim();
        return (name is "君和辰" or "陈俊杰" or "WHAPPY" or "星联达")&&password.Length==6&&password.All(c=>c is >= '0' and <= '9')&&password.Sum(c=>c-'0')==20;
    }
}

public sealed class LoginForm : Form
{
    public const string EnglishTitle="ICT Smart Coverage Recognition\n& Blind Spot Automation System";
    public const string ChineseTitle="ICT 智能覆盖率识别与盲点自动化处理系统";
    readonly Image wallpaper;
    readonly LoginCard card=new();
    readonly BreathingLogo logo=new(){BackColor=Color.Transparent};
    readonly LoginAnimatedText englishTitle=new(),chineseTitle=new(),companyTitle=new(true);
    readonly Label welcome=new(){Text="欢迎登录",BackColor=Color.Transparent,ForeColor=AppTheme.Ink,TextAlign=ContentAlignment.MiddleLeft};
    readonly Label userLabel=new(){Text="用户名",BackColor=Color.Transparent,ForeColor=AppTheme.Muted};
    readonly Label passwordLabel=new(){Text="密码",BackColor=Color.Transparent,ForeColor=AppTheme.Muted};
    readonly TextBox username=new(){Name="LoginUsername",AccessibleName="用户名",TabIndex=0,BorderStyle=BorderStyle.FixedSingle};
    readonly TextBox password=new(){Name="LoginPassword",AccessibleName="密码",UseSystemPasswordChar=true,TabIndex=1,BorderStyle=BorderStyle.FixedSingle};
    readonly CheckBox showPassword=new(){Text="显示密码",BackColor=Color.Transparent,TabIndex=2,ForeColor=AppTheme.Muted};
    readonly Label error=new(){Name="LoginError",BackColor=Color.Transparent,ForeColor=Color.FromArgb(190,58,45),TextAlign=ContentAlignment.MiddleLeft};
    readonly Button login=new(){Text="登  录",TabIndex=3,FlatStyle=FlatStyle.Flat,BackColor=AppTheme.Blue,ForeColor=Color.White,Cursor=Cursors.Hand};
    readonly System.Windows.Forms.Timer entrance=new(){Interval=42};
    readonly Stopwatch clock=new();
    readonly List<Font> ownedFonts=[];
    Bitmap? background;
    float layoutScale=1,progress,fontScale=float.NaN,breath,focusUser,focusPassword;
    Font titleFont=null!,subtitleFont=null!,smallFont=null!,companyFont=null!;
    bool loggingIn,hover,pressed;
    public const string CompanyTitle="江苏君和辰科技有限公司";
    RectangleF englishBounds,chineseBounds,companyBounds;
    Point cardPosition;
    bool ready,shown,closed;
    internal int BackgroundBuilds{get;private set;}
    internal bool AnimationRunning=>entrance.Enabled&&!loggingIn;
    internal float LoopPhase=>breath;
    public LoginForm()
    {
        using var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.login.jpg")??throw new InvalidOperationException("登录背景资源缺失");
        using var source=new Bitmap(stream);wallpaper=new Bitmap(source);
        using(var icon=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.ico"))if(icon!=null)Icon=new Icon(icon);
        using(var picture=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.png"))if(picture!=null){using var image=new Bitmap(picture);logo.Image=new Bitmap(image);}
        Text="ICT · 用户登录";AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterScreen;Size=new Size(1200,760);MinimumSize=new Size(800,560);MaximizeBox=false;DoubleBuffered=true;
        AccessibleName=ChineseTitle;login.FlatAppearance.BorderSize=0;login.Click+=(_,_)=>TryLogin();AcceptButton=login;
        showPassword.CheckedChanged+=(_,_)=>password.UseSystemPasswordChar=!showPassword.Checked;
        card.Controls.AddRange([logo,welcome,userLabel,username,passwordLabel,password,showPassword,error,login]);Controls.AddRange([card,englishTitle,chineseTitle,companyTitle]);
        login.MouseEnter+=(_,_)=>hover=true;login.MouseLeave+=(_,_)=>{hover=false;pressed=false;};login.MouseDown+=(_,_)=>pressed=true;login.MouseUp+=(_,_)=>pressed=false;
        card.Paint+=(_,e)=>{DrawFocus(e.Graphics,username,focusUser);DrawFocus(e.Graphics,password,focusPassword);};
        entrance.Tick+=(_,_)=>Animate();
        ready=true;Arrange();
    }
    void TryLogin()
    {
        if(loggingIn)return;
        if(!LoginCredentials.Validate(username.Text,password.Text)){error.Text="用户名或密码错误";password.SelectAll();password.Focus();return;}
        loggingIn=true;password.Clear();username.Enabled=password.Enabled=showPassword.Enabled=login.Enabled=false;error.ForeColor=AppTheme.Green;error.Text="登录成功，正在进入…";clock.Restart();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);if(shown)return;shown=true;
        if(StartPosition==FormStartPosition.CenterScreen)
        {
            var area=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        }
        Arrange();clock.Restart();entrance.Start();username.Focus();
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);if(background!=null&&background.Size!=ClientSize){background.Dispose();background=null;}if(!ready)return;
        if(WindowState==FormWindowState.Minimized){entrance.Stop();clock.Stop();return;}
        Arrange();ResumeAnimation();Invalidate();
    }
    void ResumeAnimation()
    {
        if(shown&&Visible&&!closed&&WindowState!=FormWindowState.Minimized){clock.Start();entrance.Start();}
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);if(!ready)return;
        if(!Visible){entrance.Stop();clock.Stop();}else ResumeAnimation();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);if(ready)Arrange();}
    void Arrange()
    {
        if(ClientSize.Width<=0||ClientSize.Height<=0||WindowState==FormWindowState.Minimized)return;
        layoutScale=Math.Min(ClientSize.Width/1200f,ClientSize.Height/720f);float s=layoutScale;
        int P(float value)=>(int)Math.Round(value*s);
        companyBounds=new RectangleF(ClientSize.Width-P(420),P(18),P(390),P(32));
        englishBounds=new RectangleF(P(58),P(65),P(760),P(92));chineseBounds=new RectangleF(P(60),P(165),P(720),P(50));
        cardPosition=new Point(P(60),P(240));card.Size=new Size(P(410),P(360));
        void Place(Control control,int x,int y,int w,int h)=>control.SetBounds(P(x),P(y),P(w),P(h));
        Place(logo,28,22,44,44);Place(welcome,84,23,290,44);Place(userLabel,28,84,350,24);Place(username,28,111,354,34);
        Place(passwordLabel,28,158,350,24);Place(password,28,185,354,34);Place(showPassword,28,230,160,26);Place(error,28,261,354,24);Place(login,28,299,354,40);
        if(fontScale!=s)
        {
            var old=ownedFonts.ToArray();ownedFonts.Clear();
            Font Make(float pixels,bool bold=false){var font=new Font("Microsoft YaHei UI",pixels*s,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);ownedFonts.Add(font);return font;}
            welcome.Font=Make(23,true);userLabel.Font=passwordLabel.Font=showPassword.Font=error.Font=Make(16);username.Font=password.Font=Make(19);login.Font=Make(19,true);
            titleFont=new Font("Segoe UI",32*s,FontStyle.Bold,GraphicsUnit.Pixel);subtitleFont=Make(21);smallFont=new Font("Segoe UI",14*s,FontStyle.Regular,GraphicsUnit.Pixel);companyFont=Make(17,true);ownedFonts.Add(titleFont);ownedFonts.Add(smallFont);
            foreach(var font in old)font.Dispose();fontScale=s;
        }
        englishTitle.Bounds=Rectangle.Ceiling(englishBounds);chineseTitle.Bounds=Rectangle.Ceiling(chineseBounds);companyTitle.Bounds=Rectangle.Ceiling(companyBounds);
        EnsureBackground();
        if(background!=null)
        {
            englishTitle.Configure(background,EnglishTitle,titleFont);chineseTitle.Configure(background,ChineseTitle,subtitleFont);companyTitle.Configure(background,CompanyTitle,companyFont);
            card.Configure(background,new Rectangle(cardPosition,new Size(card.Width,card.Height+P(20))));
        }
        MoveCard();
    }
    static Color Blend(Color a,Color b,float amount)=>Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));
    void DrawFocus(Graphics graphics,Control input,float amount)
    {
        var rect=input.Bounds;rect.Inflate(2,2);using var pen=new Pen(Blend(Color.FromArgb(218,226,238),AppTheme.Blue,amount),Math.Max(1,layoutScale));graphics.DrawRectangle(pen,rect);
    }
    void Animate()
    {
        if(loggingIn){if(clock.ElapsedMilliseconds>=300){entrance.Stop();clock.Stop();DialogResult=DialogResult.OK;Close();}return;}
        progress=Math.Min(1,(float)clock.Elapsed.TotalMilliseconds/800);breath=(float)((Math.Sin(Math.Max(0,clock.Elapsed.TotalMilliseconds-800)*Math.PI/3000)+1)/2);
        if(progress<1)MoveCard();else if(card.Location!=cardPosition){card.Location=cardPosition;card.BackgroundOffset=0;card.Invalidate(true);}
        logo.Zoom=progress<1?.85f+.15f*progress:.98f+.04f*breath;logo.Alpha=progress;logo.Invalidate();
        float phase=(float)(clock.Elapsed.TotalSeconds%6/6);
        englishTitle.Phase=phase;chineseTitle.Phase=(phase+.13f)%1;
        companyTitle.Brightness=breath;
        englishTitle.Invalidate();chineseTitle.Invalidate();companyTitle.Invalidate();
        float u=focusUser,p=focusPassword;focusUser+=(username.Focused?1-focusUser:-focusUser)*.25f;focusPassword+=(password.Focused?1-focusPassword:-focusPassword)*.25f;
        if(Math.Abs(u-focusUser)>.001f||Math.Abs(p-focusPassword)>.001f){var rect=Rectangle.Union(username.Bounds,password.Bounds);rect.Inflate(3,3);card.Invalidate(rect);}
        var target=pressed?Color.FromArgb(24,76,181):hover?Color.FromArgb(62,130,240):AppTheme.Blue;var next=Blend(login.BackColor,target,.35f);if(next!=login.BackColor)login.BackColor=next;
    }
    void MoveCard(){float eased=1-(float)Math.Pow(1-progress,3);card.BackgroundOffset=(int)(18*layoutScale*(1-eased));card.Location=new Point(cardPosition.X,cardPosition.Y+card.BackgroundOffset);card.Invalidate(true);}
    void EnsureBackground()
    {
        if(background!=null||!ready||ClientSize.Width<=0||ClientSize.Height<=0)return;
        background=new Bitmap(ClientSize.Width,ClientSize.Height);using var g=Graphics.FromImage(background);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        float factor=Math.Max((float)ClientSize.Width/wallpaper.Width,(float)ClientSize.Height/wallpaper.Height);float w=wallpaper.Width*factor,h=wallpaper.Height*factor;
        g.DrawImage(wallpaper,(ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);
        using var wash=new LinearGradientBrush(new Point(0,0),new Point((int)(ClientSize.Width*.72),0),Color.FromArgb(210,247,250,255),Color.FromArgb(0,247,250,255));g.FillRectangle(wash,0,0,(int)(ClientSize.Width*.72),ClientSize.Height);BackgroundBuilds++;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(!ready){base.OnPaintBackground(e);return;}
        EnsureBackground();
        if(background!=null)e.Graphics.DrawImage(background,e.ClipRectangle,e.ClipRectangle,GraphicsUnit.Pixel);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(!ready)return;e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var muted=new SolidBrush(AppTheme.Muted);e.Graphics.DrawString("WHAPPY  ·  ICT Coverage  ·  v0.5.6",smallFont,muted,new PointF(60*layoutScale,640*layoutScale));
    }
    protected override void OnFormClosed(FormClosedEventArgs e){closed=true;entrance.Stop();clock.Stop();password.Clear();base.OnFormClosed(e);}
    protected override void Dispose(bool disposing)
    {
        if(disposing){entrance.Dispose();background?.Dispose();wallpaper.Dispose();logo.Image?.Dispose();foreach(var font in ownedFonts)font.Dispose();}base.Dispose(disposing);
    }
}

internal sealed class BreathingLogo : Control
{
    Image? image;
    Bitmap? cachedImage;
    readonly ImageAttributes attributes=new();
    readonly ColorMatrix alphaMatrix=new();
    internal Image? Image{get=>image;set{image=value;BuildCache();}}
    internal float Zoom{get;set;}=1;
    internal float Alpha{get;set;}=1;
    internal BreathingLogo(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.SupportsTransparentBackColor,true);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);BuildCache();}
    void BuildCache()
    {
        cachedImage?.Dispose();cachedImage=null;int side=Math.Min(Width,Height);
        if(image==null||side<=0)return;
        cachedImage=new Bitmap(side,side);using var g=Graphics.FromImage(cachedImage);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,0,0,side,side);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(cachedImage==null)return;int size=(int)(Math.Min(Width,Height)*.95f*Zoom);alphaMatrix.Matrix33=Alpha;attributes.SetColorMatrix(alphaMatrix);
        e.Graphics.InterpolationMode=InterpolationMode.Bilinear;e.Graphics.DrawImage(cachedImage,new Rectangle((Width-size)/2,(Height-size)/2,size,size),0,0,cachedImage.Width,cachedImage.Height,GraphicsUnit.Pixel,attributes);
    }
    protected override void Dispose(bool disposing){if(disposing){cachedImage?.Dispose();attributes.Dispose();}base.Dispose(disposing);}
}
