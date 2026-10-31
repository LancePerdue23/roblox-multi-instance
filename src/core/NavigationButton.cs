using System.Drawing.Drawing2D;
namespace RobloxLaunchHelper;

internal sealed class NavigationButton : WorkspaceButton
{
    internal WorkspaceSection Section;
    internal bool Selected;
    private bool hovering, pressed;
    internal NavigationButton() { TextAlign = ContentAlignment.MiddleLeft; Padding = new(38,6,8,6); Cursor=Cursors.Hand; }
    protected override void OnMouseEnter(EventArgs e) { hovering=true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovering=false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) pressed=true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed=false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if(!Capture) pressed=false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if(e.KeyCode==Keys.Space) pressed=true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed=false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { pressed=false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme=ThemePalette.For(this);
        e.Graphics.Clear(Parent?.BackColor ?? theme.Surface);
        if(Selected || hovering || pressed)
        {
            using var fill=new SolidBrush(pressed?theme.Border:theme.Selection);
            using var path=WorkspaceCard.Rounded(new(0,0,Width-1,Height-1),6); e.Graphics.FillPath(fill,path);
            if(Selected) { using var accent=new Pen(theme.Accent,3); e.Graphics.DrawLine(accent,2,8,2,Height-8); }
            else { using var border=new Pen(theme.Border); e.Graphics.DrawPath(border,path); }
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(54,0,Width-62,Height),Selected?theme.Text:theme.Muted,TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),theme.Text,theme.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(ForeColor,1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = 18, y = Height/2f-8;
        switch (Section)
        {
            case WorkspaceSection.Instances:
                e.Graphics.DrawRectangle(pen,x,y,17,15); e.Graphics.DrawLine(pen,x,y+5,x+17,y+5); break;
            case WorkspaceSection.Layouts:
                for (int row=0;row<2;row++) for(int col=0;col<2;col++) e.Graphics.DrawRectangle(pen,x+col*10,y+row*10,6,6); break;
            case WorkspaceSection.Activity:
                e.Graphics.DrawLine(pen,x+2,y+16,x+2,y+10); e.Graphics.DrawLine(pen,x+8,y+16,x+8,y+2); e.Graphics.DrawLine(pen,x+14,y+16,x+14,y+6); break;
            case WorkspaceSection.System:
                e.Graphics.DrawRectangle(pen,x,y,18,12); e.Graphics.DrawLine(pen,x+9,y+12,x+9,y+17); e.Graphics.DrawLine(pen,x+4,y+17,x+14,y+17); break;
            case WorkspaceSection.Settings:
                for(int i=0;i<3;i++) { float yy=y+i*7; e.Graphics.DrawLine(pen,x,yy,x+18,yy); e.Graphics.DrawEllipse(pen,x+(i%2==0?4:11),yy-2,4,4); } break;
        }
    }
}
