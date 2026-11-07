namespace ICTCoverage.WinForms;

// Read-only report surface: work and drawing are bounded by the visible viewport, not the board size.
public sealed class ReportTable : Control
{
    public static readonly string[] ColumnNames=["元件类型","元件位置","标准值","缺件","短路","错件","空焊","盲点原因"];
    readonly VScrollBar scroll=new(){Dock=DockStyle.Right,SmallChange=1};
    readonly System.Windows.Forms.Timer wheelTimer=new(){Interval=16};
    readonly ToolTip tip=new(){InitialDelay=450,AutoPopDelay=12000,ReshowDelay=250};
    readonly Font headerFont=new("Microsoft YaHei UI",11,FontStyle.Bold);
    readonly Dictionary<(int Row,bool Selected),Bitmap> rowImages=[];
    readonly LinkedList<(int Row,bool Selected)> recent=[];
    object?[][] rows=[];int top,selected=-1,wheel,hoverRow=-1,hoverColumn=-1;
    int HeaderHeight=>LogicalToDeviceUnits(44);
    int RowHeight=>LogicalToDeviceUnits(62);
    int VisibleRows=>Math.Max(1,(Height-HeaderHeight)/RowHeight);
    int MaxTop=>Math.Max(0,rows.Length-VisibleRows);
    public int RowCount=>rows.Length;
    public long PaintedCellCount{get;private set;}
    public event EventHandler? ReasonDoubleClick;
    public int FirstDisplayedScrollingRowIndex{get=>top;set=>SetTop(value);}
    public int SelectedRowIndex{get=>selected;set{int next=rows.Length==0?-1:Math.Clamp(value,0,rows.Length-1);if(selected==next)return;int old=selected;selected=next;InvalidateRow(old);InvalidateRow(next);}}

    public ReportTable()
    {
        Dock=DockStyle.Fill;BackColor=Color.White;Font=new Font("Microsoft YaHei UI",11.5f);TabStop=true;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        Controls.Add(scroll);scroll.ValueChanged+=(_,_)=>{if(top!=scroll.Value){top=scroll.Value;tip.SetToolTip(this,null);Invalidate();}};
        wheelTimer.Tick+=(_,_)=>
        {
            wheelTimer.Stop();int notches=Math.Clamp(wheel/120,-25,25);wheel-=notches*120;
            int lines=SystemInformation.MouseWheelScrollLines;if(lines<0)lines=VisibleRows;
            int old=top;SetTop(top-notches*lines);if(top==old&&notches!=0)wheel=0;
            if(Math.Abs(wheel)>=120)wheelTimer.Start();
        };
        AccessibleRole=AccessibleRole.Table;AccessibleName="Coverage元件表";
    }
    public object? GetValue(int row,string column)=>rows[row][Array.IndexOf(ColumnNames,column)];
    public void SetRows(object?[][] values)
    {
        CancelPendingScroll();ClearImages();rows=values;top=0;selected=values.Length==0?-1:0;hoverRow=hoverColumn=-1;Reflow();
        AccessibleDescription=$"共{rows.Length}条元件记录";Invalidate();
    }
    public void CancelPendingScroll(){wheelTimer.Stop();wheel=0;}
    void Reflow()
    {
        scroll.Visible=rows.Length>VisibleRows;scroll.Maximum=Math.Max(0,rows.Length-1);scroll.LargeChange=VisibleRows;
        top=Math.Clamp(top,0,MaxTop);if(scroll.Value!=top)scroll.Value=top;
    }
    void SetTop(int value)
    {
        int next=Math.Clamp(value,0,MaxTop);if(next==top)return;top=next;if(scroll.Value!=next)scroll.Value=next;
        tip.SetToolTip(this,null);hoverRow=hoverColumn=-1;Invalidate();
    }
    int[] Widths()
    {
        int available=ClientSize.Width-(scroll.Visible?scroll.Width:0);int[] widths=[95,110,100,56,56,56,56,0];
        for(int i=0;i<7;i++)widths[i]=LogicalToDeviceUnits(widths[i]);
        widths[7]=Math.Max(1,available-widths.Take(7).Sum());return widths;
    }
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(rowImages!=null)ClearImages();Invalidate();}
    protected override void OnResize(EventArgs e){base.OnResize(e);ClearImages();Reflow();}
    protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);ClearImages();Reflow();Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if(e is HandledMouseEventArgs handled){if(handled.Handled)return;handled.Handled=true;}
        wheel=(int)Math.Clamp((long)wheel+e.Delta,-120000,120000);wheelTimer.Start();
    }
    (int Row,int Column) Hit(Point point)
    {
        int row=point.Y<HeaderHeight?-1:top+(point.Y-HeaderHeight)/RowHeight;int x=0,column=-1;var widths=Widths();
        for(int i=0;i<widths.Length;i++){x+=widths[i];if(point.X<x){column=i;break;}}
        return(row>=rows.Length?-1:row,column);
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();var hit=Hit(e.Location);if(hit.Row>=0)SelectedRowIndex=hit.Row;}
    protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);var hit=Hit(e.Location);if(hit.Row>=0&&hit.Column==7)ReasonDoubleClick?.Invoke(this,EventArgs.Empty);}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);var hit=Hit(e.Location);if(hit.Row==hoverRow&&hit.Column==hoverColumn)return;
        hoverRow=hit.Row;hoverColumn=hit.Column;tip.SetToolTip(this,hit.Row>=0&&hit.Column>=0?rows[hit.Row][hit.Column]?.ToString():null);
    }
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(rows.Length==0)return;int next=e.KeyCode switch{Keys.Up=>selected-1,Keys.Down=>selected+1,Keys.Home=>0,Keys.End=>rows.Length-1,Keys.PageUp=>selected-VisibleRows,Keys.PageDown=>selected+VisibleRows,_=>selected};
        if(next==selected)return;SelectedRowIndex=next;if(selected<top)SetTop(selected);else if(selected>=top+VisibleRows)SetTop(selected-VisibleRows+1);e.Handled=true;
    }
    protected override void OnLostFocus(EventArgs e){CancelPendingScroll();base.OnLostFocus(e);}
    protected override void OnVisibleChanged(EventArgs e){if(!Visible)CancelPendingScroll();base.OnVisibleChanged(e);}
    void ClearImages(){foreach(var image in rowImages.Values)image.Dispose();rowImages.Clear();recent.Clear();}
    void InvalidateRow(int row){if(row>=top&&row<=top+VisibleRows)Invalidate(new Rectangle(0,HeaderHeight+(row-top)*RowHeight,Width,RowHeight));}
    Bitmap RowImage(int row,int[] widths)
    {
        var key=(row,row==selected);if(rowImages.TryGetValue(key,out var cached)){recent.Remove(key);recent.AddFirst(key);return cached;}
        // GDI TextRenderer does not preserve alpha. Cache opaque rows to avoid dark fringes when compositing.
        var bitmap=new Bitmap(Math.Max(1,widths.Sum()),RowHeight,System.Drawing.Imaging.PixelFormat.Format32bppRgb);bitmap.SetResolution(DeviceDpi,DeviceDpi);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(row==selected?Color.FromArgb(219,233,255):row%2==0?Color.White:Color.FromArgb(249,251,254));
            int x=0;for(int col=0;col<8;col++)
            {
                string text=rows[row][col]?.ToString()??"";bool status=col is >=3 and <=6;var color=status?(text=="Х"?Color.FromArgb(210,91,38):text=="√"?AppTheme.Green:AppTheme.Muted):AppTheme.Ink;
                using var format=new StringFormat{Alignment=status?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=col==7?StringFormatFlags.LineLimit:StringFormatFlags.NoWrap};
                using var brush=new SolidBrush(color);
                g.DrawString(text,status?AppTheme.StatusFont:Font,brush,new RectangleF(x+6,5,Math.Max(1,widths[col]-12),RowHeight-10),format);x+=widths[col];PaintedCellCount++;
            }
            using var pen=new Pen(Color.FromArgb(231,236,244));g.DrawLine(pen,0,RowHeight-1,bitmap.Width,RowHeight-1);
        }
        rowImages[key]=bitmap;recent.AddFirst(key);
        int capacity=Math.Clamp(16*1024*1024/Math.Max(1,bitmap.Width*bitmap.Height*4),8,64);
        if(recent.Count>capacity){var expired=recent.Last!.Value;recent.RemoveLast();rowImages[expired].Dispose();rowImages.Remove(expired);}
        return bitmap;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var widths=Widths();int tableWidth=widths.Sum(),x=0;
        using var background=new SolidBrush(Color.White);e.Graphics.FillRectangle(background,e.ClipRectangle);
        var flags=TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping;
        if(e.ClipRectangle.Top<HeaderHeight)
        {
            using var header=new SolidBrush(Color.FromArgb(232,238,248));e.Graphics.FillRectangle(header,0,0,tableWidth,HeaderHeight);
            for(int col=0;col<8;col++){TextRenderer.DrawText(e.Graphics,ColumnNames[col],headerFont,new Rectangle(x+6,0,Math.Max(1,widths[col]-12),HeaderHeight),AppTheme.Ink,flags);x+=widths[col];}
        }
        int end=Math.Min(rows.Length,top+VisibleRows+1);
        for(int row=top;row<end;row++)
        {
            int y=HeaderHeight+(row-top)*RowHeight;if(!new Rectangle(0,y,tableWidth,RowHeight).IntersectsWith(e.ClipRectangle))continue;
            e.Graphics.DrawImageUnscaled(RowImage(row,widths),0,y);
        }
        if(rows.Length==0)TextRenderer.DrawText(e.Graphics,"没有符合条件的元件",Font,new Rectangle(0,HeaderHeight,tableWidth,Math.Max(1,Height-HeaderHeight)),AppTheme.Muted,flags|TextFormatFlags.HorizontalCenter);
    }
    protected override void Dispose(bool disposing){if(disposing){wheelTimer.Dispose();tip.Dispose();headerFont.Dispose();ClearImages();}base.Dispose(disposing);}
}
