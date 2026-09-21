namespace TrayGames.Core;

public sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Menu;
    public override Color MenuBorder => Theme.Separator;
    public override Color MenuItemBorder => Theme.MenuHover;
    public override Color MenuItemSelected => Theme.MenuHover;
    public override Color MenuItemSelectedGradientBegin => Theme.MenuHover;
    public override Color MenuItemSelectedGradientEnd => Theme.MenuHover;
    public override Color MenuItemPressedGradientBegin => Theme.MenuHover;
    public override Color MenuItemPressedGradientEnd => Theme.MenuHover;
    public override Color ImageMarginGradientBegin => Theme.Menu;
    public override Color ImageMarginGradientMiddle => Theme.Menu;
    public override Color ImageMarginGradientEnd => Theme.Menu;
    public override Color SeparatorDark => Theme.Separator;
    public override Color SeparatorLight => Theme.Separator;
}

public class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColorTable()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
        base.OnRenderItemText(e);
    }

    // The stock check is a light Windows checkbox, the one bright element left
    // in an otherwise dark menu.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Green, 1.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(r.Left + 3.5f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Bottom - 4.5f),
            new PointF(r.Right - 3.5f, r.Top + 4f),
        });
    }

    // The game draws its own background; a hover highlight behind it would only
    // show up as a bright halo in the item's margin.
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ToolStripControlHost or ToolStripLabel) return;
        base.OnRenderMenuItemBackground(e);
    }
}
