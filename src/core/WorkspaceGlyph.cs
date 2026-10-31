using System.Drawing.Drawing2D;
namespace RobloxLaunchHelper;

internal static class WorkspaceGlyph
{
    internal static void Draw(Graphics graphics, Rectangle bounds, string kind, Color color)
    {
        var state=graphics.Save();
        try
        {
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X,bounds.Y);
            graphics.ScaleTransform(bounds.Width/32f,bounds.Height/32f);
            using var pen=new Pen(color,2f) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round };
            if(kind=="Windows")
            {
                using var shape=WorkspaceCard.Rounded(new(3,4,26,24),3);
                graphics.DrawPath(pen,shape); graphics.DrawLine(pen,3,11,29,11);
                graphics.DrawLine(pen,7,7.5f,8,7.5f); graphics.DrawLine(pen,12,7.5f,13,7.5f);
            }
            else if(kind=="Background")
            {
                using var frame=WorkspaceCard.Rounded(new(3,4,26,24),3);
                graphics.DrawPath(pen,frame);
                graphics.DrawLines(pen,[new(8,11),new(13,16),new(8,21)]);
                graphics.DrawLine(pen,18,22,24,22);
            }
            else if(kind=="Browser integration")
            {
                // Open inner ends keep the interlocking links legible at small sizes.
                graphics.TranslateTransform(16,16); graphics.RotateTransform(-40);
                using var top=new GraphicsPath();
                top.AddLine(-6,-3,-6,-9);
                top.AddArc(-6,-15,12,12,180,180); top.AddLine(6,-9,6,-3);
                top.AddArc(-6,-9,12,12,0,135);
                graphics.DrawPath(pen,top);
                graphics.RotateTransform(180); graphics.DrawPath(pen,top);
                graphics.DrawLine(pen,0,-5,0,5);
            }
            else
            {
                // A crisp application mark, drawn at the target DPI rather than stretched artwork.
                graphics.TranslateTransform(16,16); graphics.RotateTransform(14);
                using var mark=new GraphicsPath(FillMode.Alternate);
                mark.AddRectangle(new RectangleF(-12,-12,24,24)); mark.AddRectangle(new RectangleF(-3.5f,-3.5f,7,7));
                using var brush=new SolidBrush(color); graphics.FillPath(brush,mark);
            }
        }
        finally { graphics.Restore(state); }
    }
}
