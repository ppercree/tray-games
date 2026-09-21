using System.Drawing.Drawing2D;
using TrayGames.Core;

namespace TrayGames.Launcher;

/// Small stand-in icons. These are deliberately redrawn rather than imported
/// from the games: reusing their real icons would mean loading seven game
/// assemblies into the launcher just to fill 40 pixels.
internal static class Glyphs
{
    public static void Paint(string key, Graphics g, RectangleF box, Color accent)
    {
        var state = g.Save();
        g.TranslateTransform(box.X, box.Y);
        float u = box.Width / 32f;

        switch (key)
        {
            case "snake": Snake(g, u); break;
            case "mine": Mine(g, u, accent); break;
            case "tetris": Tetris(g, u, accent); break;
            case "ttt": Ttt(g, u); break;
            case "four": Four(g, u); break;
            case "koi": Koi(g, u); break;
            case "stacks": Stacks(g, u); break;
        }

        g.Restore(state);
    }

    static void Snake(Graphics g, float u)
    {
        using var pen = new Pen(Theme.Green, 6 * u)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new[]
        {
            new PointF(7 * u, 24 * u), new PointF(19 * u, 24 * u), new PointF(19 * u, 14 * u),
        });
        using var food = new SolidBrush(Theme.Red);
        g.FillEllipse(food, 6 * u, 6 * u, 8 * u, 8 * u);
    }

    static void Mine(Graphics g, float u, Color accent)
    {
        Draw.FillRounded(g, new RectangleF(5 * u, 5 * u, 22 * u, 22 * u), 5 * u, Theme.Tile);
        using (var pole = new Pen(Theme.Text, 2 * u))
            g.DrawLine(pole, 14 * u, 10 * u, 14 * u, 22 * u);
        using var flag = new SolidBrush(accent);
        g.FillPolygon(flag, new[]
        {
            new PointF(14 * u, 10 * u), new PointF(23 * u, 14 * u), new PointF(14 * u, 18 * u),
        });
    }

    static void Tetris(Graphics g, float u, Color accent)
    {
        void B(float x, float y, Color c) =>
            Draw.FillRounded(g, new RectangleF(x * u, y * u, 8 * u, 8 * u), 2 * u, c);
        B(5, 14, accent);
        B(14, 14, accent);
        B(23, 14, accent);
        B(14, 5, accent);
        B(5, 23, Theme.Cyan);
        B(14, 23, Theme.Cyan);
        B(23, 23, Theme.Cyan);
    }

    static void Ttt(Graphics g, float u)
    {
        using (var grid = new Pen(Theme.TextDim, 1.6f * u))
        {
            g.DrawLine(grid, 14 * u, 5 * u, 14 * u, 27 * u);
            g.DrawLine(grid, 22 * u, 5 * u, 22 * u, 27 * u);
            g.DrawLine(grid, 5 * u, 14 * u, 27 * u, 14 * u);
            g.DrawLine(grid, 5 * u, 22 * u, 27 * u, 22 * u);
        }
        using (var x = new Pen(Theme.Cyan, 2.6f * u))
        {
            g.DrawLine(x, 7 * u, 7 * u, 12 * u, 12 * u);
            g.DrawLine(x, 12 * u, 7 * u, 7 * u, 12 * u);
        }
        using var o = new Pen(Theme.Orange, 2.6f * u);
        g.DrawEllipse(o, 23.5f * u, 23.5f * u, 5 * u, 5 * u);
    }

    static void Four(Graphics g, float u)
    {
        Draw.FillRounded(g, new RectangleF(4 * u, 7 * u, 24 * u, 21 * u), 4 * u, Theme.Tile);
        var slots = new[]
        {
            (6f, 9f, Theme.BoardInner), (13f, 9f, Theme.BoardInner), (20f, 9f, Theme.Red),
            (6f, 16f, Theme.BoardInner), (13f, 16f, Theme.Red), (20f, 16f, Theme.Yellow),
            (6f, 23f, Theme.Yellow), (13f, 23f, Theme.Yellow), (20f, 23f, Theme.Red),
        };
        foreach (var (x, y, c) in slots)
        {
            using var b = new SolidBrush(c);
            g.FillEllipse(b, x * u, y * u, 6 * u, 6 * u);
        }
    }

    static void Koi(Graphics g, float u)
    {
        using (var water = new SolidBrush(Color.FromArgb(20, 56, 62)))
            g.FillEllipse(water, 3 * u, 3 * u, 26 * u, 26 * u);
        using (var body = new SolidBrush(Color.FromArgb(244, 246, 248)))
            g.FillEllipse(body, 11 * u, 12 * u, 15 * u, 8 * u);
        using (var tail = new SolidBrush(Theme.Orange))
            g.FillPolygon(tail, new[]
            {
                new PointF(13 * u, 16 * u), new PointF(6 * u, 11 * u), new PointF(6 * u, 21 * u),
            });
        using var patch = new SolidBrush(Color.FromArgb(232, 120, 62));
        g.FillEllipse(patch, 16 * u, 12 * u, 6 * u, 4.5f * u);
    }

    static void Stacks(Graphics g, float u)
    {
        void B(float x, float y, float w, Color c) =>
            Draw.FillRounded(g, new RectangleF(x * u, y * u, w * u, 5 * u), 1.4f * u, c);
        B(5, 23, 22, Theme.Cyan);
        B(7, 17, 18, Theme.Blue);
        B(10, 11, 13, Theme.Purple);
        B(13, 5, 8, Theme.Red);
    }
}
