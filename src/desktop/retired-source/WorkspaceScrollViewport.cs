using System.Drawing.Drawing2D;

namespace RobloxLaunchHelper;

// Layouts uses client-area scrolling only. No WS_VSCROLL range or native scrollbar
// exists for Windows to repaint during activation, layout, or theme transitions.
internal sealed class WorkspaceScrollViewport : Panel
{
    private readonly Control content;
    internal readonly WorkspaceVerticalScrollBar Scrollbar = new();
    private bool arranging;
    private int offset;
    internal int MaximumOffset { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal new Point AutoScrollPosition
    {
        get => new(0,-offset);
        set => ScrollTo(value.Y);
    }
    internal WorkspaceScrollViewport(Control content)
    {
        this.content=content;
        AutoScroll=false; Tag="surface"; Margin=new(0); AccessibleName="Layout options";
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
        content.Dock=DockStyle.None; content.AutoSize=false;
        Controls.Add(content); Controls.Add(Scrollbar); Scrollbar.BringToFront();
        content.Layout+=(_,_)=>Reflow();
        Scrollbar.ScrollRequested+=ScrollTo;
        WatchFocus(content);
    }
    private void WatchFocus(Control parent)
    {
        parent.Enter+=(_,_)=>ScrollControlIntoView(parent);
        parent.ControlAdded+=(_,e)=> { if(e.Control is not null) WatchFocus(e.Control); };
        foreach(Control child in parent.Controls) WatchFocus(child);
    }
    protected override void OnLayout(LayoutEventArgs e)
    { base.OnLayout(e); if(content is not null) Reflow(); }
    private void Reflow()
    {
        if(arranging || IsDisposed) return;
        arranging=true;
        try
        {
            int barWidth=LogicalToDeviceUnits(17);
            int height=content.GetPreferredSize(new(Math.Max(1,ClientSize.Width),0)).Height;
            bool overflow=height>ClientSize.Height;
            int width=Math.Max(1,ClientSize.Width-(overflow?barWidth:0));
            height=Math.Max(content.MinimumSize.Height,content.GetPreferredSize(new(width,0)).Height);
            MaximumOffset=Math.Max(0,height-ClientSize.Height);
            offset=Math.Clamp(offset,0,MaximumOffset);
            content.SetBounds(0,-offset,width,height);
            Scrollbar.SetBounds(width,0,barWidth,ClientSize.Height);
            Scrollbar.Visible=MaximumOffset>0;
            Scrollbar.SetRange(offset,MaximumOffset,ClientSize.Height);
        }
        finally { arranging=false; }
    }
    internal void ScrollTo(int value)
    {
        offset=Math.Clamp(value,0,MaximumOffset);
        content.Top=-offset;
        Scrollbar.SetRange(offset,MaximumOffset,ClientSize.Height);
    }
    internal void ScrollBy(int delta) => ScrollTo(offset+delta);
    public new void ScrollControlIntoView(Control? target)
    {
        if(target is null || target==content || !Contains(target) || !target.Visible || !IsHandleCreated) return;
        var bounds=RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
        if(bounds.Top<0) ScrollBy(bounds.Top);
        else if(bounds.Bottom>ClientSize.Height) ScrollBy(bounds.Bottom-ClientSize.Height);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(-Math.Sign(e.Delta)*LogicalToDeviceUnits(48)*Math.Max(1,Math.Abs(e.Delta)/120));
        if(e is HandledMouseEventArgs handled) handled.Handled=true;
        base.OnMouseWheel(e);
    }
}

internal sealed class WorkspaceVerticalScrollBar : Control
{
    private int value,maximum,page,dragOffset;
    private bool dragging;
    private int repeatStep;
    private readonly System.Windows.Forms.Timer repeat=new() { Interval=400 };
    internal event Action<int>? ScrollRequested;
    internal WorkspaceVerticalScrollBar()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        TabStop=true; AccessibleRole=AccessibleRole.ScrollBar; AccessibleName="Layout options scrollbar";
        repeat.Tick+=(_,_)=> { repeat.Interval=60; ScrollRequested?.Invoke(value+repeatStep); };
    }
    internal void SetRange(int value,int maximum,int page)
    {
        this.value=value; this.maximum=maximum; this.page=page; Invalidate();
    }
    private int Arrow => Math.Min(Width,Height/2);
    private Rectangle Thumb
    {
        get
        {
            int length=Math.Max(0,Height-2*Arrow);
            int size=Math.Min(length,Math.Max(LogicalToDeviceUnits(24),(int)((long)length*page/Math.Max(1,(long)maximum+page))));
            int top=Arrow+(int)((long)(length-size)*value/Math.Max(1,maximum));
            int inset=Math.Max(2,Width*3/10);
            return new(inset,top,Math.Max(1,Width-2*inset),size);
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    { using var fill=new SolidBrush(BackColor); e.Graphics.FillRectangle(fill,e.ClipRectangle); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var palette=ThemePalette.For(this);
        var thumb=Thumb;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        if(thumb.Height>0)
        {
            using var path=WorkspaceCard.Rounded(thumb,thumb.Width/2);
            using var fill=new SolidBrush(dragging?palette.Accent:palette.Muted);
            e.Graphics.FillPath(fill,path);
        }
        using var pen=new Pen(palette.Muted,Math.Max(1,DeviceDpi/96f));
        float center=Width/2f,half=LogicalToDeviceUnits(2);
        foreach(bool down in new[]{false,true})
        {
            float y=down?Height-Arrow/2f:Arrow/2f,direction=down?1:-1;
            PointF[] points=[new(center-half,y-direction),new(center,y+direction),new(center+half,y-direction)];
            e.Graphics.DrawLines(pen,points);
        }
        if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-1,-1),palette.Text,BackColor);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if(e.Button!=MouseButtons.Left) return;
        Focus();
        var thumb=Thumb;
        if(e.Y>=thumb.Top && e.Y<thumb.Bottom)
        { dragging=true; dragOffset=e.Y-thumb.Top; Capture=true; Invalidate(); }
        else
        {
            repeatStep=e.Y<Arrow?-LogicalToDeviceUnits(32):e.Y>=Height-Arrow?LogicalToDeviceUnits(32):e.Y<thumb.Top?-page:page;
            Capture=true; ScrollRequested?.Invoke(value+repeatStep); repeat.Interval=400; repeat.Start();
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(!dragging) return;
        int travel=Height-2*Arrow-Thumb.Height;
        ScrollRequested?.Invoke((int)Math.Clamp((long)(e.Y-Arrow-dragOffset)*maximum/Math.Max(1,travel),0,maximum));
    }
    protected override void OnMouseUp(MouseEventArgs e)
    { base.OnMouseUp(e); if(e.Button==MouseButtons.Left) { repeat.Stop(); dragging=false; Capture=false; Invalidate(); } }
    protected override void OnMouseCaptureChanged(EventArgs e)
    { base.OnMouseCaptureChanged(e); if(!Capture) { repeat.Stop(); dragging=false; Invalidate(); } }
    protected override void Dispose(bool disposing)
    { if(disposing) repeat.Dispose(); base.Dispose(disposing); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int? next=e.KeyCode switch { Keys.Up=>value-LogicalToDeviceUnits(32),Keys.Down=>value+LogicalToDeviceUnits(32),
            Keys.PageUp=>value-page,Keys.PageDown=>value+page,Keys.Home=>0,Keys.End=>maximum,_=>null };
        if(next is int target) { ScrollRequested?.Invoke(target); e.Handled=true; }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override AccessibleObject CreateAccessibilityInstance() => new ScrollAccessibility(this);
    private sealed class ScrollAccessibility(WorkspaceVerticalScrollBar owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get { int current=owner.value; return current.ToString(); } set { if(int.TryParse(value,out int target)) owner.ScrollRequested?.Invoke(Math.Clamp(target,0,owner.maximum)); } }
        public override string? DefaultAction => "Scroll down";
        public override void DoDefaultAction() => owner.ScrollRequested?.Invoke(owner.value+owner.page);
    }
}
