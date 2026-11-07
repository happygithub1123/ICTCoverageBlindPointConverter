using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;

namespace ICTCoverage.WinForms;

internal static class LoginCredentials
{
    // Local startup gate; never persist the entered password in rules, sessions or logs.
    internal static bool Validate(string user,string password)
    {
        byte[] actual=SHA256.HashData(Encoding.UTF8.GetBytes(password));
        byte[] expected=Convert.FromHexString("8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92");
        return string.Equals(user.Trim(),"陈俊杰",StringComparison.Ordinal)&&CryptographicOperations.FixedTimeEquals(actual,expected);
    }
}

public sealed class LoginForm : Form
{
    public const string EnglishTitle="ICT Smart Coverage Recognition\n& Blind Spot Automation System";
    public const string ChineseTitle="ICT 智能覆盖率识别与盲点自动化处理系统";
    readonly Image wallpaper;
    readonly Panel card=new(){BackColor=Color.FromArgb(250,252,255)};
    readonly PictureBox logo=new(){SizeMode=PictureBoxSizeMode.Zoom};
    readonly Label welcome=new(){Text="欢迎登录",ForeColor=AppTheme.Ink,TextAlign=ContentAlignment.MiddleLeft};
    readonly Label userLabel=new(){Text="用户名",ForeColor=AppTheme.Muted},passwordLabel=new(){Text="密码",ForeColor=AppTheme.Muted};
    readonly TextBox username=new(){Name="LoginUsername",Text="陈俊杰",TabIndex=0,BorderStyle=BorderStyle.FixedSingle};
    readonly TextBox password=new(){Name="LoginPassword",UseSystemPasswordChar=true,TabIndex=1,BorderStyle=BorderStyle.FixedSingle};
    readonly CheckBox showPassword=new(){Text="显示密码",TabIndex=2,ForeColor=AppTheme.Muted};
    readonly Label error=new(){Name="LoginError",ForeColor=Color.FromArgb(190,58,45),TextAlign=ContentAlignment.MiddleLeft};
    readonly Button login=new(){Text="登  录",TabIndex=3,FlatStyle=FlatStyle.Flat,BackColor=AppTheme.Blue,ForeColor=Color.White,Cursor=Cursors.Hand};
    readonly System.Windows.Forms.Timer entrance=new(){Interval=33};
    readonly Stopwatch clock=new();
    readonly List<Font> ownedFonts=[];
    Bitmap? background;
    float layoutScale=1,progress,fontScale=float.NaN;
    RectangleF englishBounds,chineseBounds;
    Point cardPosition;
    bool ready,shown;
    internal int BackgroundBuilds{get;private set;}
    internal bool AnimationRunning=>entrance.Enabled;
    public LoginForm()
    {
        using var stream=GetType().Assembly.GetManifestResourceStream("ICTCoverage.login.jpg")??throw new InvalidOperationException("登录背景资源缺失");
        using var source=new Bitmap(stream);wallpaper=new Bitmap(source);
        using(var icon=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.ico"))if(icon!=null)Icon=new Icon(icon);
        using(var picture=GetType().Assembly.GetManifestResourceStream("ICTCoverage.whappy.png"))if(picture!=null){using var image=new Bitmap(picture);logo.Image=new Bitmap(image);}
        Text="ICT · 用户登录";AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterScreen;Size=new Size(1200,760);MinimumSize=new Size(800,560);MaximizeBox=false;DoubleBuffered=true;
        AccessibleName=ChineseTitle;login.FlatAppearance.BorderSize=0;login.Click+=(_,_)=>TryLogin();AcceptButton=login;
        showPassword.CheckedChanged+=(_,_)=>password.UseSystemPasswordChar=!showPassword.Checked;
        card.Controls.AddRange([logo,welcome,userLabel,username,passwordLabel,password,showPassword,error,login]);Controls.Add(card);
        entrance.Tick+=(_,_)=>
        {
            progress=Math.Min(1,(float)clock.Elapsed.TotalMilliseconds/800);MoveCard();Invalidate(Rectangle.Ceiling(RectangleF.Union(englishBounds,chineseBounds)));
            if(progress>=1){entrance.Stop();clock.Stop();}
        };
        ready=true;Arrange();
    }
    void TryLogin()
    {
        if(!LoginCredentials.Validate(username.Text,password.Text)){error.Text="用户名或密码错误";password.SelectAll();password.Focus();return;}
        password.Clear();DialogResult=DialogResult.OK;Close();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);if(shown)return;shown=true;
        if(StartPosition==FormStartPosition.CenterScreen)
        {
            var area=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        }
        Arrange();clock.Restart();entrance.Start();password.Focus();
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);background?.Dispose();background=null;if(ready)Arrange();Invalidate();}
    protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);if(ready)Arrange();}
    void Arrange()
    {
        if(ClientSize.Width<=0||ClientSize.Height<=0||WindowState==FormWindowState.Minimized)return;
        layoutScale=Math.Min(ClientSize.Width/1200f,ClientSize.Height/720f);float s=layoutScale;
        int P(float value)=>(int)Math.Round(value*s);
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
            foreach(var font in old)font.Dispose();fontScale=s;
        }
        MoveCard();
    }
    void MoveCard(){float eased=1-(float)Math.Pow(1-progress,3);card.Location=new Point(cardPosition.X,cardPosition.Y+(int)(18*layoutScale*(1-eased)));}
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(!ready){base.OnPaintBackground(e);return;}
        if(background==null&&ClientSize.Width>0&&ClientSize.Height>0)
        {
            background=new Bitmap(ClientSize.Width,ClientSize.Height);using var g=Graphics.FromImage(background);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            float factor=Math.Max((float)ClientSize.Width/wallpaper.Width,(float)ClientSize.Height/wallpaper.Height);float w=wallpaper.Width*factor,h=wallpaper.Height*factor;
            g.DrawImage(wallpaper,(ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);
            using var wash=new LinearGradientBrush(new Point(0,0),new Point((int)(ClientSize.Width*.72),0),Color.FromArgb(210,247,250,255),Color.FromArgb(0,247,250,255));g.FillRectangle(wash,0,0,(int)(ClientSize.Width*.72),ClientSize.Height);BackgroundBuilds++;
        }
        if(background!=null)e.Graphics.DrawImageUnscaled(background,0,0);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(!ready)return;e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        int alpha=(int)(255*progress);using var ink=new SolidBrush(Color.FromArgb(alpha,AppTheme.Ink));using var title=new Font("Segoe UI",32*layoutScale,FontStyle.Bold,GraphicsUnit.Pixel);using var subtitle=new Font("Microsoft YaHei UI",21*layoutScale,FontStyle.Regular,GraphicsUnit.Pixel);
        e.Graphics.DrawString(EnglishTitle,title,ink,englishBounds);e.Graphics.DrawString(ChineseTitle,subtitle,ink,chineseBounds);
        using var small=new Font("Segoe UI",14*layoutScale,FontStyle.Regular,GraphicsUnit.Pixel);using var muted=new SolidBrush(AppTheme.Muted);
        e.Graphics.DrawString("WHAPPY  ·  ICT Coverage  ·  v0.5.4",small,muted,new PointF(60*layoutScale,640*layoutScale));
    }
    protected override void OnFormClosed(FormClosedEventArgs e){entrance.Stop();clock.Stop();password.Clear();base.OnFormClosed(e);}
    protected override void Dispose(bool disposing)
    {
        if(disposing){entrance.Dispose();background?.Dispose();wallpaper.Dispose();logo.Image?.Dispose();foreach(var font in ownedFonts)font.Dispose();}base.Dispose(disposing);
    }
}
