using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

// Paint the existing scrollbar geometry. Windows retains hit testing, dragging,
// keyboard/wheel input, range calculations and accessibility for each control.
internal sealed class WorkspaceScrollbars : NativeWindow
{
    private static readonly ConditionalWeakTable<Control, WorkspaceScrollbars> windows = new();
    private readonly Control control;
    private readonly System.Windows.Forms.Timer tracking = new() { Interval = 30 };
    private bool painting;
    private bool repaintQueued;
    private int handleGeneration;

    internal static void Apply(Control control)
    {
        if (control is not (ScrollableControl or ListView or TextBoxBase or ScrollBar or DataGridView)) return;
        windows.GetValue(control, c => new WorkspaceScrollbars(c)).Paint();
    }

    private WorkspaceScrollbars(Control control)
    {
        this.control = control;
        control.HandleCreated += (_, _) => Attach();
        control.HandleDestroyed += (_, _) => { handleGeneration++; repaintQueued=false; tracking.Stop(); ReleaseHandle(); };
        control.Disposed += (_, _) => { tracking.Dispose(); ReleaseHandle(); };
        // WinForms updates native ranges with SetScrollInfo after layout. That API
        // can draw a scrollbar directly without sending a paint message to us.
        control.Layout += (_, _) => QueuePaint();
        control.VisibleChanged += (_, _) => QueuePaint();
        // DataGridView creates scrollbar children; later children need the same palette.
        control.ControlAdded += (_, e) =>
        {
            if (e.Control is ScrollBar bar) ThemePalette.For(control).Apply(bar);
        };
        tracking.Tick += (_, _) =>
        {
            if (Control.MouseButtons == MouseButtons.None) tracking.Stop();
            Paint();
        };
        if (control.IsHandleCreated) Attach();
    }

    private void Attach()
    {
        if (Handle != 0) ReleaseHandle();
        AssignHandle(control.Handle);
        handleGeneration++;
        QueuePaint();
    }

    private void QueuePaint()
    {
        if(repaintQueued || Handle==0 || control.IsDisposed || SystemInformation.HighContrast) return;
        repaintQueued=true;
        int generation=handleGeneration;
        control.BeginInvoke(() =>
        {
            if(generation!=handleGeneration || control.IsDisposed) return;
            repaintQueued=false;
            Paint();
        });
    }

    protected override void WndProc(ref Message message)
    {
        if (!SystemInformation.HighContrast)
        {
            // Activation can redraw the native frame directly, without WM_NCPAINT.
            // Own both paths so it cannot replace the themed track with a black one.
            if (message.Msg is 0x0085 or 0x0086 && OwnsNonClientPaint(control))
            {
                Paint();
                message.Result = message.Msg == 0x0086 ? 1 : 0;
                return;
            }
            if (control is ScrollBar)
            {
                if (message.Msg == 0x0014) { message.Result = 1; return; } // WM_ERASEBKGND
                if (message.Msg == 0x000f)
                {
                    var dc = BeginPaint(Handle, out var paint);
                    try { if (dc != 0) Paint(dc); }
                    finally { EndPaint(Handle, ref paint); }
                    message.Result = 0;
                    return;
                }
                if (message.Msg is 0x0317 or 0x0318)
                {
                    Paint(message.WParam);
                    message.Result = 0;
                    return;
                }
            }
        }
        // The native scrollbar runs a modal tracking loop. Repaint throughout that
        // loop as well as after normal paints so dragging cannot restore native colors.
        if (!SystemInformation.HighContrast && (message.Msg == 0x00a1 || control is ScrollBar && message.Msg == 0x0201))
            tracking.Start();
        base.WndProc(ref message);
        if (message.Msg == 0x0317) Paint(message.WParam); // WM_PRINT uses window coordinates.
        else if (message.Msg is 0x000f or 0x0085 or 0x0086 or 0x0005 or 0x0047 or 0x031a or 0x0114 or 0x0115 or 0x020a or 0x020e or 0x00a0 or 0x0200 or 0x02a2 or 0x02a3 or 0x0007 or 0x0008 or 0x0128 or 0x0215)
            Paint();
    }

    // These controls have no other frame to paint. Bordered controls and form
    // captions retain their native nonclient painting, as does high contrast.
    internal static bool OwnsNonClientPaint(Control control) => control switch
    {
        Panel panel => panel.BorderStyle == BorderStyle.None,
        UserControl page => page.BorderStyle == BorderStyle.None,
        TextBoxBase text => text.BorderStyle == BorderStyle.None,
        ListView list => list.BorderStyle == BorderStyle.None,
        _ => false
    };

    private void Paint(nint printDc = default)
    {
        if (painting || Handle == 0 || control.IsDisposed || SystemInformation.HighContrast) return;
        painting = true;
        nint dc = printDc != 0 ? printDc : GetWindowDC(Handle);
        try
        {
            if (dc == 0 || !GetWindowRect(Handle, out var window)) return;
            using var graphics = Graphics.FromHdc(dc);
            graphics.SmoothingMode = SmoothingMode.None;
            if (control is DataGridView grid)
            {
                var vertical = grid.Controls.OfType<VScrollBar>().FirstOrDefault(b => b.Visible);
                var horizontal = grid.Controls.OfType<HScrollBar>().FirstOrDefault(b => b.Visible);
                if (vertical is not null && horizontal is not null)
                {
                    var origin = grid.PointToScreen(Point.Empty);
                    using var fill = new SolidBrush(grid.BackgroundColor);
                    graphics.FillRectangle(fill, new Rectangle(origin.X - window.Left + vertical.Left,
                        origin.Y - window.Top + horizontal.Top, vertical.Width, horizontal.Height));
                }
            }
            if (control is ScrollBar bar)
                PaintBar(graphics, -4, bar is VScrollBar, window); // OBJID_CLIENT
            else
            {
                var vertical = PaintBar(graphics, -5, true, window);
                var horizontal = PaintBar(graphics, -6, false, window);
                if (vertical is Rectangle v && horizontal is Rectangle h)
                {
                    using var fill = new SolidBrush(control.BackColor);
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.FillRectangle(fill, new Rectangle(v.X, h.Y, v.Width, h.Height));
                }
            }
        }
        finally
        {
            if (printDc == 0 && dc != 0) ReleaseDC(Handle, dc);
            painting = false;
        }
    }

    private Rectangle? PaintBar(Graphics graphics, int objectId, bool vertical, NativeRect window)
    {
        var info = new ScrollbarInfo { Size = Marshal.SizeOf<ScrollbarInfo>() };
        if (!GetScrollBarInfo(Handle, objectId, ref info) || (info.State & 0x00008000) != 0) return null; // Invisible
        var bounds = Rectangle.FromLTRB(info.Rect.Left - window.Left, info.Rect.Top - window.Top,
            info.Rect.Right - window.Left, info.Rect.Bottom - window.Top);
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var theme = ThemePalette.For(control);
        using var surface = new SolidBrush(control.BackColor);
        // The track must cover every edge pixel. Antialias only the thumb and arrows;
        // smoothing this fill can blend the native black frame into its left edge.
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.FillRectangle(surface, bounds);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if ((info.State & 1) != 0 || !control.Enabled) return bounds; // Unavailable; blend empty track into surface.
        float scale = control.DeviceDpi / 96f;
        int thickness = vertical ? bounds.Width : bounds.Height;
        int inset = Math.Max(2, (int)(thickness * .3));
        int start = vertical ? bounds.Top : bounds.Left;
        int length = vertical ? bounds.Height : bounds.Width;
        int arrowLength = Math.Clamp(info.LineButton, 0, length / 2);
        int top = Math.Clamp(info.ThumbTop, arrowLength, length - arrowLength);
        int bottom = Math.Clamp(info.ThumbBottom, top, length - arrowLength);
        var thumb = vertical ? new Rectangle(bounds.Left + inset, start + top, thickness - 2 * inset, bottom - top)
            : new Rectangle(start + top, bounds.Top + inset, bottom - top, thickness - 2 * inset);
        bool active = tracking.Enabled;
        if (thumb.Width > 0 && thumb.Height > 0)
        {
            using var shape = WorkspaceCard.Rounded(thumb, Math.Min(thumb.Width, thumb.Height) / 2);
            using var fill = new SolidBrush(active ? theme.Accent : Blend(theme.Muted, control.BackColor, .65f));
            graphics.FillPath(fill, shape);
        }
        if (arrowLength > 0)
        {
            using var pen = new Pen(Blend(theme.Muted, control.BackColor, .65f), Math.Max(1, 1.3f * scale))
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float half = Math.Max(2, 2.5f * scale);
            void Arrow(float position, bool forward)
            {
                float x = vertical ? bounds.Left + bounds.Width / 2f : position;
                float y = vertical ? position : bounds.Top + bounds.Height / 2f;
                float direction = forward ? 1 : -1;
                PointF[] points = vertical
                    ? [new(x - half, y - direction * half / 2), new(x, y + direction * half / 2), new(x + half, y - direction * half / 2)]
                    : [new(x - direction * half / 2, y - half), new(x + direction * half / 2, y), new(x - direction * half / 2, y + half)];
                graphics.DrawLines(pen, points);
            }
            Arrow(start + arrowLength / 2f, false);
            Arrow(start + length - arrowLength / 2f, true);
        }
        return bounds;
    }

    private static Color Blend(Color foreground, Color background, float amount) => Color.FromArgb(
        (int)(foreground.R * amount + background.R * (1 - amount)),
        (int)(foreground.G * amount + background.G * (1 - amount)),
        (int)(foreground.B * amount + background.B * (1 - amount)));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PaintInfo
    {
        internal nint Dc;
        internal int Erase;
        internal NativeRect Rect;
        internal int Restore, IncrementalUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollbarInfo
    {
        internal int Size;
        internal NativeRect Rect;
        internal int LineButton, ThumbTop, ThumbBottom, Reserved;
        internal uint State, UpState, UpPageState, ThumbState, DownPageState, DownState;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollBarInfo(nint window, int objectId, ref ScrollbarInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint window, out PaintInfo paint);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint window, ref PaintInfo paint);
}
