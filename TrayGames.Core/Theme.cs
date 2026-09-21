namespace TrayGames.Core;

public static class Theme
{
    public static readonly Color Menu = Color.FromArgb(26, 27, 30);
    public static readonly Color MenuHover = Color.FromArgb(48, 50, 56);
    public static readonly Color Separator = Color.FromArgb(58, 60, 66);
    public static readonly Color Text = Color.FromArgb(226, 228, 233);
    public static readonly Color TextDim = Color.FromArgb(142, 146, 155);

    public static readonly Color Board = Color.FromArgb(26, 27, 30);
    public static readonly Color BoardInner = Color.FromArgb(17, 18, 21);
    public static readonly Color Tile = Color.FromArgb(46, 48, 54);
    public static readonly Color TileLit = Color.FromArgb(62, 65, 72);

    public static readonly Color Green = Color.FromArgb(126, 231, 135);
    public static readonly Color GreenDark = Color.FromArgb(38, 112, 70);
    public static readonly Color Red = Color.FromArgb(240, 98, 92);
    public static readonly Color Yellow = Color.FromArgb(242, 201, 96);
    public static readonly Color Blue = Color.FromArgb(104, 168, 245);
    public static readonly Color Purple = Color.FromArgb(178, 138, 240);
    public static readonly Color Orange = Color.FromArgb(240, 150, 76);
    public static readonly Color Cyan = Color.FromArgb(104, 214, 224);

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
