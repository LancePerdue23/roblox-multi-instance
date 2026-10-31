using System.Runtime.InteropServices;
namespace RobloxLaunchHelper;

// Keep native selection, keyboard navigation and accessibility, but buffer list painting.
internal sealed class WorkspaceListView : ListView
{
    private readonly HeaderWindow header;
    private readonly System.Windows.Forms.Timer resizeRepair = new() { Interval=50 };
    private bool rebuildingViewport;
    private bool fittingColumns;

    internal WorkspaceListView()
    {
        header=new HeaderWindow(this);
        resizeRepair.Tick+=(_,_)=>FinishResize();
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window,int message,nint wParam,nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct StyleChange { internal uint OldStyle, NewStyle; }
    private void FitInstanceColumns(int outerWidth)
    {
        if (fittingColumns || Tag as string!="instances" || Columns.Count!=6) return;
        fittingColumns=true;
        try
        {
            int width=Math.Max(0,outerWidth-SystemInformation.VerticalScrollBarWidth-4);
            int title=(int)(width*.48), cpu=(int)(width*.14), memory=(int)(width*.18);
            int[] widths=[title,0,0,cpu,memory,width-title-cpu-memory];
            for(int i=0;i<widths.Length;i++)
                if(Columns[i].Width>widths[i]) Columns[i].Width=widths[i];
            for(int i=0;i<widths.Length;i++)
                if(Columns[i].Width<widths[i]) Columns[i].Width=widths[i];
        }
        finally { fittingColumns=false; }
    }
    protected override void SetBoundsCore(int x,int y,int width,int height,BoundsSpecified specified)
    {
        // ClientSizeChanged is too late: the native resize has already introduced
        // a horizontal scrollbar when the old columns exceed the incoming width.
        if (width<Width) FitInstanceColumns(width);
        base.SetBoundsCore(x,y,width,height,specified);
        FitInstanceColumns(Width);
    }
    protected override void OnClientSizeChanged(EventArgs e)
    {
        FitInstanceColumns(Width);
        base.OnClientSizeChanged(e);
    }
    internal void FinishResize()
    {
        resizeRepair.Stop();
        if (!IsHandleCreated || IsDisposed || View!=View.Details) return;
        // Bound native viewport repairs during a drag, without waiting for mouse-up.
        rebuildingViewport=true;
        try { RecreateHandle(); }
        finally { rebuildingViewport=false; }
        Invalidate(true);
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!rebuildingViewport && IsHandleCreated && !IsDisposed && resizeRepair is not null)
            resizeRepair.Start();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) resizeRepair.Dispose();
        base.Dispose(disposing);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitInstanceColumns(Width);
        var handle=SendMessage(Handle,0x101f,0,0); // LVM_GETHEADER
        if(handle!=0) header.AssignHandle(handle);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        header.ReleaseHandle(); base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message)
    {
        // This responsive table always fits its columns. Reject horizontal scrollbar
        // style changes before Windows lays out or paints the non-client area.
        if (message.Msg==0x007c && message.WParam==-16 && message.LParam!=0 && Tag as string=="instances")
        {
            var style=Marshal.PtrToStructure<StyleChange>(message.LParam);
            style.NewStyle &= ~0x00100000u; // WS_HSCROLL; retain WS_VSCROLL.
            Marshal.StructureToPtr(style,message.LParam,false);
        }
        base.WndProc(ref message);
        if(message.Msg is not (0x000f or 0x0318 or 0x0317) || !IsHandleCreated || View!=View.Details) return;
        // Explorer's native theme draws column dividers into empty space even with GridLines=false.
        // Paint only the unused area, leaving native row interaction and scrolling intact.
        int top=Items.Count>0?GetItemRect(Items.Count-1).Bottom:Font.Height+12;
        if(top>=ClientSize.Height) return;
        using var graphics=message.Msg is 0x0318 or 0x0317 && message.WParam!=0
            ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle);
        using var fill=new SolidBrush(BackColor);
        graphics.FillRectangle(fill,new Rectangle(0,Math.Max(0,top),ClientSize.Width,ClientSize.Height-Math.Max(0,top)));
    }
    private sealed class HeaderWindow(WorkspaceListView owner) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if(message.Msg is not (0x000f or 0x0318 or 0x0317)) return;
            int right=owner.Columns.Cast<ColumnHeader>().Sum(column=>column.Width);
            if(right>=owner.ClientSize.Width) return;
            using var graphics=message.Msg is 0x0318 or 0x0317 && message.WParam!=0
                ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle);
            using var fill=new SolidBrush(owner.BackColor);
            graphics.FillRectangle(fill,right,0,owner.ClientSize.Width-right,owner.Font.Height+24);
        }
    }
}
