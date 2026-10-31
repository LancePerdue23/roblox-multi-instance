using System.Drawing.Drawing2D;
namespace RobloxLaunchHelper;

internal class WorkspaceButton : Button
{
    private bool hovering;
    internal bool ActiveChoice;
    internal bool HideMnemonicUnderline;
    internal WorkspaceButton()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; AutoSize = true;
    }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = ThemePalette.For(this);
        var fill = !Enabled?theme.Surface:ActiveChoice?theme.Selection:hovering && Enabled ? theme.Selection : BackColor;
        if (hovering && Enabled && Text.Contains("Close window")) fill = Color.FromArgb(150,45,52);
        e.Graphics.Clear(Parent?.BackColor ?? theme.Surface);
        var bounds = new Rectangle(0,0,Width-1,Height-1);
        using var shape = WorkspaceCard.Rounded(bounds, 8);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(fill); using var border = new Pen(ActiveChoice?theme.Accent:theme.Border); e.Graphics.FillPath(brush,shape); e.Graphics.DrawPath(border,shape);
        var textBounds = new Rectangle(Padding.Left,Padding.Top,Width-Padding.Horizontal,Height-Padding.Vertical);
        TextRenderer.DrawText(e.Graphics,AccessibleName=="Launch Roblox"?"＋  "+Text:Text=="Move up"?"↑":Text=="Move down"?"↓":Text,Font,textBounds,Enabled ? ForeColor : theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (UseMnemonic ? (HideMnemonicUnderline ? TextFormatFlags.HidePrefix : 0) : TextFormatFlags.NoPrefix) | (TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(bounds,-4,-4),theme.Text,fill);
    }
}
