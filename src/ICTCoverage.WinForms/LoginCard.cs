namespace ICTCoverage.WinForms;

// Cache the wallpaper + 50% white layer rather than asking the form to repaint
// through transparent child windows on every animation frame.
internal sealed class LoginCard : Panel
{
    Bitmap? backdrop;
    internal int BackgroundOffset { get; set; }
    internal LoginCard()=>SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    internal void Configure(Bitmap background,Rectangle source)
    {
        backdrop?.Dispose();backdrop=new Bitmap(source.Width,source.Height);
        using var g=Graphics.FromImage(backdrop);
        g.DrawImage(background,new Rectangle(Point.Empty,source.Size),source,GraphicsUnit.Pixel);
        using var white=new SolidBrush(Color.FromArgb(128,250,252,255));
        g.FillRectangle(white,new Rectangle(Point.Empty,source.Size));Invalidate(true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(backdrop==null){base.OnPaintBackground(e);return;}
        var source=e.ClipRectangle;source.Offset(0,BackgroundOffset);
        e.Graphics.DrawImage(backdrop,e.ClipRectangle,source,GraphicsUnit.Pixel);
    }
    protected override void Dispose(bool disposing){if(disposing)backdrop?.Dispose();base.Dispose(disposing);}
}
