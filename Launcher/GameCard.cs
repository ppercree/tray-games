using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TrayGames.Core;

namespace TrayGames.Launcher;

internal sealed class GameCard : Control
{
    static readonly Font NameFont = new("Segoe UI", 10f, FontStyle.Bold);
    static readonly Font BlurbFont = new("Segoe UI", 7.5f);
    static readonly Font StateFont = new("Segoe UI", 7.5f, FontStyle.Bold);

    readonly GameEntry entry;
    bool hover;
    bool overClose;
    bool overGear;
    bool running;

    public GameCard(GameEntry entry)
    {
        this.entry = entry;
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        Size = new Size(216, 92);
        Cursor = Cursors.Hand;
    }

    public event EventHandler? StateChanged;
    public event EventHandler? SettingsRequested;

    public void Refresh(bool isRunning)
    {
        if (running == isRunning) return;
        running = isRunning;
        Invalidate();
    }

    Rectangle CloseButton => new(Width - 28, 10, 18, 18);
    Rectangle GearButton => new(Width - 52, 10, 18, 18);

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        overClose = false;
        overGear = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool over = running && CloseButton.Contains(e.Location);
        bool overGear = GearButton.Contains(e.Location);
        if (over != overClose || overGear != this.overGear)
        {
            overClose = over;
            this.overGear = overGear;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!entry.Installed) return;

        if (GearButton.Contains(e.Location))
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (running)
        {
            // A running game is only dismissed from its own small button, so a
            // stray click on the card cannot kill a game mid-move.
            if (CloseButton.Contains(e.Location)) entry.Dismiss();
            else return;
        }
        else
        {
            entry.Summon();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Three sliders rather than a cogwheel: at 18 pixels a cog is mush.
    void DrawGear(Graphics g)
    {
        var r = GearButton;
        if (overGear) Draw.FillRounded(g, r, 4, Theme.TileLit);

        var colour = overGear ? Theme.Text : Theme.TextDim;
        using var line = new Pen(colour, 1.4f);
        using var knob = new SolidBrush(colour);

        for (int i = 0; i < 3; i++)
        {
            float y = r.Top + 5 + i * 4.5f;
            g.DrawLine(line, r.Left + 3, y, r.Right - 3, y);
            float kx = r.Left + 4 + (i == 1 ? 8 : i == 0 ? 3 : 6);
            g.FillEllipse(knob, kx, y - 1.6f, 3.2f, 3.2f);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(Theme.Menu))
            g.FillRectangle(bg, ClientRectangle);

        var card = new RectangleF(0, 0, Width - 1, Height - 1);
        Draw.FillRounded(g, card, 8, hover && entry.Installed ? Theme.TileLit : Theme.Tile);
        if (running)
            Draw.DrawRounded(g, card, 8, Color.FromArgb(130, entry.Accent), 1.4f);

        var iconBox = new RectangleF(14, 26, 40, 40);
        Draw.FillRounded(g, iconBox, 8, Theme.BoardInner);
        Glyphs.Paint(entry.Glyph, g, iconBox, entry.Accent);

        using (var name = new SolidBrush(entry.Installed ? Theme.Text : Theme.TextDim))
            g.DrawString(entry.Name, NameFont, name, 64, 22);

        // Clipped to one line: a wrapped blurb would run straight into the
        // status line below it.
        using (var blurb = new SolidBrush(Theme.TextDim))
        using (var oneLine = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            g.DrawString(entry.Blurb, BlurbFont, blurb, new RectangleF(64, 42, Width - 74, 15), oneLine);

        string state = !entry.Installed ? "not built" : running ? "in your tray" : "click to summon";
        var stateColor = !entry.Installed ? Theme.Red : running ? entry.Accent : Theme.TextDim;

        if (running)
        {
            using var dot = new SolidBrush(entry.Accent);
            g.FillEllipse(dot, 64, 71, 6, 6);
        }

        using (var brush = new SolidBrush(stateColor))
            g.DrawString(state, StateFont, brush, running ? 74 : 62, 66);

        DrawGear(g);

        if (!running) return;

        var close = CloseButton;
        if (overClose) Draw.FillRounded(g, close, 4, Color.FromArgb(90, Theme.Red));
        using var pen = new Pen(overClose ? Theme.Red : Theme.TextDim, 1.6f);
        g.DrawLine(pen, close.Left + 5, close.Top + 5, close.Right - 5, close.Bottom - 5);
        g.DrawLine(pen, close.Right - 5, close.Top + 5, close.Left + 5, close.Bottom - 5);
    }
}
