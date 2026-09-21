using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TrayGames.Core;

namespace TrayGames.Launcher;

/// The same options a game shows in its own tray menu, laid out as rows of
/// pills. Picking one writes the game's settings file immediately, which a
/// running game picks up on its own.
internal sealed class SettingsPanel : Control
{
    static readonly Font LabelFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    static readonly Font PillFont = new("Segoe UI", 8.5f);

    const int SideMargin = 18;
    const int PillHeight = 26;
    const int PillGap = 6;

    readonly string title;
    readonly List<Setting> settings;
    readonly Color accent;
    readonly List<(RectangleF rect, Setting setting, int index)> hits = new();

    int hover = -1;

    public SettingsPanel(string title, List<Setting> settings, Color accent)
    {
        this.title = title;
        this.settings = settings;
        this.accent = accent;

        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Menu;
    }

    public event EventHandler? Changed;

    /// Measures and positions every pill, and returns the height needed. Layout
    /// and hit-testing share one list so a pill can never be drawn somewhere it
    /// cannot be clicked.
    public int LayoutPills(int width)
    {
        hits.Clear();
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);

        float y = 14;
        foreach (var setting in settings)
        {
            y += 18;   // label line
            float x = SideMargin;
            float rowHeight = PillHeight;

            for (int i = 0; i < setting.Options.Length; i++)
            {
                float w = Math.Max(52, g.MeasureString(setting.Options[i], PillFont).Width + 22);
                if (x + w > width - SideMargin && x > SideMargin)
                {
                    x = SideMargin;
                    y += PillHeight + PillGap;
                    rowHeight += PillHeight + PillGap;
                }
                hits.Add((new RectangleF(x, y, w, PillHeight), setting, i));
                x += w + PillGap;
            }

            y += PillHeight + 16;
        }

        return (int)y + 4;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int found = hits.FindIndex(h => h.rect.Contains(e.Location));
        if (found == hover) return;
        hover = found;
        Cursor = found >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int found = hits.FindIndex(h => h.rect.Contains(e.Location));
        if (found < 0) return;

        var (_, setting, index) = hits[found];
        if (setting.Index == index) return;

        setting.Index = index;
        GameEntry.SaveSettings(title, settings);
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(Theme.Menu))
            g.FillRectangle(bg, ClientRectangle);

        var labelled = new HashSet<Setting>();
        foreach (var (rect, setting, index) in hits)
        {
            if (labelled.Add(setting))
                using (var brush = new SolidBrush(Theme.TextDim))
                    g.DrawString(setting.Label, LabelFont, brush, SideMargin, rect.Y - 20);

            bool chosen = setting.Index == index;
            bool hot = hover >= 0 && hits[hover].rect == rect;

            var fill = chosen ? Color.FromArgb(60, accent) : hot ? Theme.TileLit : Theme.Tile;
            Draw.FillRounded(g, rect, 6, fill);
            if (chosen) Draw.DrawRounded(g, rect, 6, accent, 1.4f);

            Draw.CenteredText(g, setting.Options[index], PillFont,
                chosen ? Theme.Text : Theme.TextDim, rect);
        }
    }
}

internal sealed class SettingsDialog : Form
{
    public SettingsDialog(GameEntry entry, string title, List<Setting> settings)
    {
        Text = $"{entry.Name} settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Menu;
        ForeColor = Theme.Text;

        var panel = new SettingsPanel(title, settings, entry.Accent)
        {
            Location = new Point(0, 34),
            Width = 400,
        };
        int height = panel.LayoutPills(400);
        panel.Height = height;

        ClientSize = new Size(400, 34 + height + 44);
        Controls.Add(panel);

        var close = new Button
        {
            Text = "Done",
            Size = new Size(88, 28),
            Location = new Point(400 - 88 - 18, 34 + height + 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Tile,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand,
        };
        close.FlatAppearance.BorderColor = Theme.Separator;
        close.FlatAppearance.MouseOverBackColor = Theme.TileLit;
        close.Click += (_, _) => Close();
        Controls.Add(close);

        AcceptButton = close;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var brush = new SolidBrush(Theme.TextDim))
            e.Graphics.DrawString("applies immediately, even while the game is running",
                new Font("Segoe UI", 7.5f), brush, 18, ClientSize.Height - 36);
    }
}
