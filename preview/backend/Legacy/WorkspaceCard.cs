using System.Drawing.Drawing2D;
namespace RobloxLaunchHelper;

internal class WorkspaceCard : Panel
{
    internal WorkspaceCard()
    {
        // Rounded edges move on resize; repaint the old edge as well as the newly exposed strip.
        SetStyle(ControlStyles.ResizeRedraw, true);
        DoubleBuffered=true; Padding=new(20); Margin=new(0);
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // Docked children can retain background pixels copied from the old rounded edge.
        // Repaint this card's children without compositing the entire workspace window.
        Invalidate(true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var theme=ThemePalette.For(this);
        // Respect the paint clip and translated origin when transparent children ask us
        // to paint their background; Graphics.Clear clears the drawing surface instead.
        using var background=new SolidBrush(Parent?.BackColor ?? theme.Background);
        e.Graphics.FillRectangle(background,ClientRectangle);
        using var path=Rounded(new(0,0,Width-1,Height-1),8);
        using var fill=new SolidBrush(theme.Surface); using var border=new Pen(theme.Border);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; e.Graphics.FillPath(fill,path); e.Graphics.DrawPath(border,path);
    }
    internal static GraphicsPath Rounded(Rectangle bounds,int radius)
    {
        var path=new GraphicsPath(); int d=Math.Max(1,Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height)));
        path.AddArc(bounds.X,bounds.Y,d,d,180,90); path.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);
        path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90); path.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
}

// Composite this small card's child controls into one frame during selection changes.
// Keep the native lists and the rest of the workspace outside this paint boundary.
internal sealed class SelectionCard : WorkspaceCard
{
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x02000000; return parameters; }
    }
}

// This plain status surface does not need the transparent label's parent repaint.
internal sealed class WorkspaceStatusLabel : Label
{
    internal WorkspaceStatusLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var fill = new SolidBrush(Parent?.BackColor ?? BackColor);
        e.Graphics.FillRectangle(fill, e.ClipRectangle);
    }
}

// Grayscale antialiasing avoids ClearType color fringes on large display text.
internal sealed class WorkspaceLabel : Label
{
    internal WorkspaceLabel() { UseCompatibleTextRendering = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var state = e.Graphics.Save();
        try { e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; base.OnPaint(e); }
        finally { e.Graphics.Restore(state); }
    }
}

internal static class WorkspaceDesign
{
    internal static Label Label(string text,float size=12,bool bold=false) => new WorkspaceLabel()
        { Text=text,AutoSize=true,Font=new("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new(0,0,0,12) };
    internal static TableLayoutPanel Stack(params Control[] controls)
    {
        var stack=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=controls.Length,AutoScroll=false,Margin=new(0),Tag="surface" };
        stack.ColumnStyles.Add(new(SizeType.Percent,100));
        for(int i=0;i<controls.Length;i++) { stack.RowStyles.Add(new(SizeType.AutoSize)); stack.Controls.Add(controls[i],0,i); }
        return stack;
    }
    internal static WorkspaceCard Card(Control content,Padding? padding=null)
    {
        var card=new WorkspaceCard { Dock=DockStyle.Fill,Padding=padding??new(24) }; card.Controls.Add(content); return card;
    }
}

internal sealed class StatusCard : WorkspaceCard
{
    private readonly Label caption=WorkspaceDesign.Label("",11);
    private readonly Label value=WorkspaceDesign.Label("—",22,true);
    private readonly bool hasAction;
    internal StatusCard(string title, Control? action = null)
    {
        hasAction = action is not null;
        Padding=new(78,8,22,8); AccessibleName=title+" summary"; caption.Text=title; caption.Tag="muted";
        caption.Margin=new(0,0,0,4); value.Margin=new(0);
        Control valueRow = value;
        if (action is not null)
        {
            Padding = new(22,8,22,8);
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new(0), Tag = "surface" };
            row.ColumnStyles.Add(new(SizeType.Percent,100)); row.ColumnStyles.Add(new(SizeType.AutoSize));
            action.Anchor = AnchorStyles.Right; action.Margin = new(8,0,0,0);
            row.Controls.Add(value,0,0); row.Controls.Add(action,1,0);
            valueRow = row;
        }
        var stack=WorkspaceDesign.Stack(caption,valueRow); Controls.Add(stack);
    }
    internal void SetValue(string text) { value.Text=text; AccessibleDescription=text; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (hasAction) return;
        var theme=ThemePalette.For(this);
        int size=LogicalToDeviceUnits(32);
        WorkspaceGlyph.Draw(e.Graphics,new(LogicalToDeviceUnits(22),(Height-size)/2,size,size),caption.Text,theme.Accent);
    }
}

// Keep native checkbox keyboard and accessibility behavior while drawing a switch.
internal sealed class WorkspaceToggle : CheckBox
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 15 };
    private bool hovering, busy;
    private float position;
    private int phase;
    [System.ComponentModel.DefaultValue(false)]
    internal bool Busy
    {
        get => busy;
        set { busy = value; AccessibleDescription = value ? "Changing browser integration…" : "Enable or disable browser integration."; Animate(); }
    }
    internal WorkspaceToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false; Size = new(56,36); Cursor = Cursors.Hand;
        animation.Tick += (_, _) =>
        {
            float target = Checked ? 1 : 0;
            position += Math.Clamp(target-position,-.12f,.12f);
            phase = (phase+12)%360;
            if (!busy && Math.Abs(target-position)<.01f) { position=target; animation.Stop(); }
            Invalidate();
        };
    }
    private void Animate()
    {
        if (IsHandleCreated && !IsDisposed) animation.Start();
        else position = Checked ? 1 : 0;
        Invalidate();
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Animate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = ThemePalette.For(this);
        e.Graphics.Clear(Parent?.BackColor ?? theme.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int inset = LogicalToDeviceUnits(4), height = LogicalToDeviceUnits(26);
        var bounds = new Rectangle(inset,(Height-height)/2,Width-inset*2-1,height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var track = WorkspaceCard.Rounded(bounds,height/2);
        var trackColor = Checked && (Enabled || busy) ? theme.Accent : theme.Border;
        if (Enabled && hovering) trackColor = ControlPaint.Light(trackColor,.15f);
        using var fill = new SolidBrush(trackColor);
        using var border = new Pen(Enabled && hovering ? theme.Accent : trackColor);
        e.Graphics.FillPath(fill,track); e.Graphics.DrawPath(border,track);
        int diameter = height-inset*2;
        float left = bounds.Left+inset+position*(bounds.Width-inset*2-diameter);
        var thumbBounds = new RectangleF(left,bounds.Top+inset,diameter,diameter);
        using var shadow = new SolidBrush(Color.FromArgb(35,Color.Black));
        e.Graphics.FillEllipse(shadow,left,bounds.Top+inset+LogicalToDeviceUnits(1),diameter,diameter);
        using var thumb = new SolidBrush(Enabled || busy ? Color.White : theme.Muted);
        e.Graphics.FillEllipse(thumb,thumbBounds);
        if (busy)
        {
            using var spinner = new Pen(theme.Accent,LogicalToDeviceUnits(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            thumbBounds.Inflate(-LogicalToDeviceUnits(4),-LogicalToDeviceUnits(4));
            e.Graphics.DrawArc(spinner,thumbBounds,phase,240);
        }
        if (Focused && ShowFocusCues)
        {
            using var focus = WorkspaceCard.Rounded(Rectangle.Inflate(bounds,LogicalToDeviceUnits(2),LogicalToDeviceUnits(2)),height/2+LogicalToDeviceUnits(2));
            using var focusPen = new Pen(theme.Accent,LogicalToDeviceUnits(1));
            e.Graphics.DrawPath(focusPen,focus);
        }
    }
}
