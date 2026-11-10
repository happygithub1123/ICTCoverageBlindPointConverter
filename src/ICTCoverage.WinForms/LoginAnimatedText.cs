using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ICTCoverage.WinForms;

// Opaque child surfaces keep animation invalidation away from the wallpaper/form.
// Glyphs, brushes and the matching wallpaper crop are rebuilt only during layout.
internal sealed class LoginAnimatedText : Control
{
    readonly List<(GraphicsPath Path, SolidBrush Brush)> runs=[];
    Bitmap? backdrop;
    Bitmap? overlay;
    Bitmap? flowOverlay;
    int[] glyphAlpha=[],pixels=[];
    int[] columnStrength=[],columnRed=[],columnGreen=[],columnBlue=[];
    readonly ImageAttributes attributes=new();
    readonly ColorMatrix alphaMatrix=new();
    float bandWidth,flowWidth;
    float flowStart;
    readonly bool company;
    internal float Phase { get; set; }
    internal float Brightness { get; set; }
    internal int CacheBuilds { get; private set; }

    internal LoginAnimatedText(bool company=false)
    {
        this.company=company;TabStop=false;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque,true);
    }

    internal void Configure(Bitmap background,string text,Font font)
    {
        ClearCache();if(Width<=0||Height<=0)return;
        backdrop=background.Clone(Bounds,background.PixelFormat);
        if(company)
        {
            string[] parts=["江苏","君和辰","科技","有限公司"];
            Color[] colors=[Color.FromArgb(50,121,220),Color.FromArgb(24,156,128),Color.FromArgb(183,127,30),Color.FromArgb(50,121,220)];
            using var g=Graphics.FromImage(backdrop);
            float[] widths=parts.Select(t=>g.MeasureString(t,font,int.MaxValue,StringFormat.GenericTypographic).Width).ToArray();
            float x=Width-widths.Sum();
            for(int i=0;i<parts.Length;i++)
            {
                var path=new GraphicsPath();path.AddString(parts[i],font.FontFamily,(int)font.Style,font.Size,new PointF(x,0),StringFormat.GenericTypographic);
                runs.Add((path,new SolidBrush(colors[i])));x+=widths[i];
            }
        }
        else
        {
            var path=new GraphicsPath();path.AddString(text,font.FontFamily,(int)font.Style,font.Size,new RectangleF(0,0,Width,Height),StringFormat.GenericDefault);
            runs.Add((path,new SolidBrush(Color.Empty)));
            flowWidth=Math.Min(Width,path.GetBounds().Right+2);
        }
        var glyphBounds=runs.Select(run=>run.Path.GetBounds()).Aggregate(RectangleF.Union);
        flowStart=Math.Max(0,glyphBounds.Left-4);
        flowWidth=Math.Max(1,Math.Min(Width-flowStart,glyphBounds.Right-flowStart+4));
        bandWidth=Math.Max(60,flowWidth*.42f);
        // Rasterize antialiased glyphs once. Re-filling long vector paths every
        // frame is expensive even when the paths themselves are cached.
        using(var g=Graphics.FromImage(backdrop))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using var ink=new LinearGradientBrush(ClientRectangle,Color.FromArgb(29,56,101),Color.FromArgb(19,108,113),LinearGradientMode.Horizontal);
            foreach(var run in runs)g.FillPath(company?run.Brush:ink,run.Path);
        }
        overlay=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(overlay))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            foreach(var run in runs)g.FillPath(Brushes.White,run.Path);
        }
        glyphAlpha=new int[Width*Height];pixels=new int[glyphAlpha.Length];columnStrength=new int[Width];
        columnRed=new int[Width];columnGreen=new int[Width];columnBlue=new int[Width];
        if(company)flowOverlay=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        {
            var data=overlay.LockBits(ClientRectangle,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(data.Scan0,glyphAlpha,0,glyphAlpha.Length);}finally{overlay.UnlockBits(data);}
            for(int i=0;i<glyphAlpha.Length;i++)glyphAlpha[i]=(int)((uint)glyphAlpha[i]>>24);
        }
        CacheBuilds++;Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(backdrop==null)return;
        e.Graphics.DrawImageUnscaled(backdrop,0,0);
        if(company&&overlay!=null)
        {
            alphaMatrix.Matrix33=.18f+.24f*Brightness;attributes.SetColorMatrix(alphaMatrix);
            e.Graphics.DrawImage(overlay,ClientRectangle,0,0,Width,Height,GraphicsUnit.Pixel,attributes);
            DrawFlow(e.Graphics,flowOverlay??overlay,true);
        }
        else if(overlay!=null)
        {
            DrawFlow(e.Graphics,overlay,false);
        }
        base.OnPaint(e);
    }
    void DrawFlow(Graphics graphics,Bitmap target,bool companyFlow)
    {
        float x=flowStart+Phase*(flowWidth+bandWidth)-bandWidth;
        int left=Math.Max(0,(int)x),right=Math.Min(Width,(int)Math.Ceiling(x+bandWidth));
        Array.Clear(pixels);
        // Company text uses the same moving cyan highlight with a brighter
        // white core; its existing blue/green/gold segments remain visible.
        for(int col=left;col<right;col++)
        {
            double wave=Math.Sin(Math.Clamp((col-x)/bandWidth,0,1)*Math.PI);
            double core=wave*wave*wave*wave;
            columnStrength[col]=(int)(255*wave);
            columnRed[col]=(int)(companyFlow?120+110*core:8+56*core);
            columnGreen[col]=(int)(companyFlow?225+30*core:205+25*core);
            columnBlue[col]=(int)(companyFlow?225+30*core:215+40*core);
        }
        for(int row=0;row<Height;row++)for(int col=left;col<right;col++)
        {
            int index=row*Width+col,a=glyphAlpha[index]*columnStrength[col]/255;
            pixels[index]=(a<<24)|((columnRed[col]*a/255)<<16)|((columnGreen[col]*a/255)<<8)|(columnBlue[col]*a/255);
        }
        var data=target.LockBits(ClientRectangle,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        try{Marshal.Copy(pixels,0,data.Scan0,pixels.Length);}finally{target.UnlockBits(data);}
        graphics.DrawImageUnscaled(target,0,0);
    }
    void ClearCache()
    {
        backdrop?.Dispose();backdrop=null;overlay?.Dispose();overlay=null;flowOverlay?.Dispose();flowOverlay=null;glyphAlpha=[];pixels=[];columnStrength=[];columnRed=[];columnGreen=[];columnBlue=[];
        foreach(var run in runs){run.Path.Dispose();run.Brush.Dispose();}runs.Clear();
    }
    protected override void Dispose(bool disposing){if(disposing){ClearCache();attributes.Dispose();}base.Dispose(disposing);}
}
