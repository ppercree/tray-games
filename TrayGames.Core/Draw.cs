using System.Drawing.Drawing2D;

namespace TrayGames.Core;

public static class Draw
{
    public static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        if (radius <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = RoundedPath(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = RoundedPath(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static void CenteredText(Graphics g, string text, Font font, Color color, RectangleF area)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, brush, area, format);
    }
}
