using System.Reflection;
using System.Runtime.InteropServices;

namespace ICTCoverage.WinForms;

// Keep fast wheel/scrollbar input from causing a separate layout and repaint for every message.
internal sealed class ResponsiveGrid : DataGridView
{
    System.Windows.Forms.Timer? scrollTimer;
    int wheelDelta,wheelX,wheelY;
    readonly ScrollHook verticalHook,horizontalHook;

    public ResponsiveGrid()
    {
        DoubleBuffered=true;AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;
        ShowCellErrors=false;ShowRowErrors=false;
        verticalHook=new(VerticalScrollBar,ScheduleScroll);horizontalHook=new(HorizontalScrollBar,ScheduleScroll);
    }

    void ScheduleScroll()
    {
        if(scrollTimer==null){scrollTimer=new(){Interval=16};scrollTimer.Tick+=(_,_)=>FlushScroll();}
        scrollTimer.Start();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if(e is HandledMouseEventArgs handled){if(handled.Handled)return;handled.Handled=true;}
        if(ModifierKeys!=Keys.None)
        {
            FlushScroll();base.OnMouseWheel(new MouseEventArgs(e.Button,e.Clicks,e.X,e.Y,e.Delta));return;
        }
        wheelDelta=(int)Math.Clamp((long)wheelDelta+e.Delta,-120000,120000);wheelX=e.X;wheelY=e.Y;
        ScheduleScroll();
    }

    void FlushScroll()
    {
        scrollTimer?.Stop();if(IsDisposed||!IsHandleCreated)return;
        verticalHook.Flush();horizontalHook.Flush();
        if(wheelDelta!=0)
        {
            int delta=Math.Clamp(wheelDelta,-3000,3000);wheelDelta-=delta;
            int previous=FirstDisplayedScrollingRowIndex;
            base.OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,wheelX,wheelY,delta));
            if(FirstDisplayedScrollingRowIndex==previous&&Math.Abs(delta)>=120)wheelDelta=0;
            if(wheelDelta!=0)scrollTimer?.Start();
        }
    }

    public void CancelPendingScroll(){scrollTimer?.Stop();wheelDelta=0;verticalHook.Cancel();horizontalHook.Cancel();}
    protected override void OnVisibleChanged(EventArgs e){if(!Visible)CancelPendingScroll();base.OnVisibleChanged(e);}
    protected override void OnLostFocus(EventArgs e){FlushScroll();CancelPendingScroll();base.OnLostFocus(e);}
    protected override void Dispose(bool disposing){if(disposing){scrollTimer?.Dispose();verticalHook.Dispose();horizontalHook.Dispose();}base.Dispose(disposing);}

    // Snapshot the full 32-bit thumb position while the native drag notification is valid.
    // Do not replay the message later: WinForms would reread a changed native track position.
    sealed class ScrollHook : NativeWindow,IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ScrollInfo{public uint Size,Mask;public int Minimum,Maximum;public uint Page;public int Position,TrackPosition;}
        [DllImport("user32.dll")]static extern bool GetScrollInfo(IntPtr handle,int kind,ref ScrollInfo info);
        static readonly Action<ScrollBar,ScrollEventArgs> RaiseScroll=typeof(ScrollBar).GetMethod("OnScroll",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<Action<ScrollBar,ScrollEventArgs>>();
        readonly ScrollBar bar;readonly Action schedule;int? pending;
        public ScrollHook(ScrollBar bar,Action schedule)
        {
            this.bar=bar;this.schedule=schedule;bar.HandleCreated+=Created;bar.HandleDestroyed+=Destroyed;
            if(bar.IsHandleCreated)AssignHandle(bar.Handle);
        }
        void Created(object? sender,EventArgs e)=>AssignHandle(bar.Handle);
        void Destroyed(object? sender,EventArgs e){pending=null;ReleaseHandle();}
        public void QueuePosition(int value){pending=value;schedule();}
        public void Cancel()=>pending=null;
        public void Flush()
        {
            if(pending is not int value||bar.IsDisposed)return;pending=null;
            value=Math.Clamp(value,bar.Minimum,Math.Max(bar.Minimum,bar.Maximum-bar.LargeChange+1));
            var args=new ScrollEventArgs(ScrollEventType.ThumbTrack,bar.Value,value,bar is HScrollBar?ScrollOrientation.HorizontalScroll:ScrollOrientation.VerticalScroll);
            RaiseScroll(bar,args);bar.Value=args.NewValue;
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg is 0x2115 or 0x2114)
            {
                long code=(long)m.WParam&0xffff;
                if(code==5)
                {
                    var info=new ScrollInfo{Size=(uint)Marshal.SizeOf<ScrollInfo>(),Mask=0x10};
                    if(GetScrollInfo(Handle,2,ref info))
                    {
                        int value=info.TrackPosition;
                        if(bar.RightToLeft==RightToLeft.Yes&&bar is HScrollBar)value=bar.Minimum+(bar.Maximum-bar.LargeChange+1)-value;
                        QueuePosition(value);m.Result=IntPtr.Zero;return;
                    }
                }
                if(code is 4 or 8)Flush();
            }
            base.WndProc(ref m);
        }
        public void Dispose(){bar.HandleCreated-=Created;bar.HandleDestroyed-=Destroyed;pending=null;ReleaseHandle();}
    }
}
