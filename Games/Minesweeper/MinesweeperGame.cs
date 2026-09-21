using TrayGames.Core;

namespace TrayGames.Minesweeper;

internal enum Phase { Playing, Won, Lost }

internal sealed class MinesweeperGame : GameControl
{
    int grid = 9;
    int mines = 10;
    int cellPx = 20;

    readonly Setting sizeSetting;
    readonly Setting mineSetting;

    static readonly Color[] NumberColors =
    {
        Theme.TextDim,      // unused, index 0
        Theme.Blue,
        Theme.Green,
        Theme.Red,
        Theme.Purple,
        Theme.Orange,
        Theme.Cyan,
        Theme.Text,
        Theme.TextDim,
    };

    bool[,] mine = new bool[9, 9];
    bool[,] revealed = new bool[9, 9];
    bool[,] flagged = new bool[9, 9];
    readonly Random rng = new();

    Phase phase;
    bool minesPlaced;
    int flags;
    int cursorX, cursorY;
    bool cursorVisible;
    Point blast = new(-1, -1);
    int best;

    public MinesweeperGame() : base(180, 180)
    {
        best = ScoreStore.Load(Title);
        sizeSetting = AddSetting("size", "Board", new[] { "8x8", "9x9", "12x12", "16x16" }, 1);
        mineSetting = AddSetting("mines", "Mines", new[] { "Few", "Normal", "Many" }, 1);
        ApplySettings();
    }

    protected override void ApplySettings()
    {
        // Cell size follows the grid so the board stays roughly 190px wide at
        // every setting; a 16x16 board at 20px would not fit in a tray menu.
        (grid, cellPx) = sizeSetting.Index switch
        {
            0 => (8, 24),
            2 => (12, 16),
            3 => (16, 12),
            _ => (9, 20),
        };

        double density = mineSetting.Index switch
        {
            0 => 0.10,
            2 => 0.19,
            _ => 0.14,
        };
        mines = Math.Max(1, (int)Math.Round(grid * grid * density));

        mine = new bool[grid, grid];
        revealed = new bool[grid, grid];
        flagged = new bool[grid, grid];

        SetBoardSize(grid * cellPx, grid * cellPx);
        NewGame();
    }

    public override string Title => "Minesweeper";

    public override string Status => phase switch
    {
        Phase.Won => best > 0 ? $"cleared!      wins {best}" : "cleared!",
        Phase.Lost => "boom - space to retry",
        _ => $"mines {mines - flags}      wins {best}",
    };

    public override void NewGame()
    {
        Array.Clear(mine);
        Array.Clear(revealed);
        Array.Clear(flagged);
        minesPlaced = false;
        flags = 0;
        phase = Phase.Playing;
        blast = new Point(-1, -1);
        cursorX = cursorY = grid / 2;
        Announce();
    }

    // mines are placed after the first reveal so the opening click can never
    // lose, which is how every minesweeper worth playing behaves.
    void PlaceMines(int safeX, int safeY)
    {
        int placed = 0;
        while (placed < mines)
        {
            int x = rng.Next(grid), y = rng.Next(grid);
            if (mine[x, y]) continue;
            if (Math.Abs(x - safeX) <= 1 && Math.Abs(y - safeY) <= 1) continue;
            mine[x, y] = true;
            placed++;
        }
        minesPlaced = true;
    }

    int Adjacent(int x, int y)
    {
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < grid && ny < grid && mine[nx, ny]) n++;
            }
        return n;
    }

    void Reveal(int x, int y)
    {
        if (phase != Phase.Playing || revealed[x, y] || flagged[x, y]) return;
        if (!minesPlaced) PlaceMines(x, y);

        if (mine[x, y])
        {
            revealed[x, y] = true;
            blast = new Point(x, y);
            phase = Phase.Lost;
            for (int j = 0; j < grid; j++)
                for (int i = 0; i < grid; i++)
                    if (mine[i, j]) revealed[i, j] = true;
            Announce();
            return;
        }

        // Iterative flood fill: a recursive one is fine at 9x9 but this keeps
        // the shape of the board a free parameter.
        var queue = new Queue<Point>();
        queue.Enqueue(new Point(x, y));
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (revealed[p.X, p.Y] || flagged[p.X, p.Y]) continue;
            revealed[p.X, p.Y] = true;
            if (Adjacent(p.X, p.Y) != 0) continue;

            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = p.X + dx, ny = p.Y + dy;
                    if (nx >= 0 && ny >= 0 && nx < grid && ny < grid && !revealed[nx, ny])
                        queue.Enqueue(new Point(nx, ny));
                }
        }

        CheckWin();
        Announce();
    }

    void ToggleFlag(int x, int y)
    {
        if (phase != Phase.Playing || revealed[x, y]) return;
        flagged[x, y] = !flagged[x, y];
        flags += flagged[x, y] ? 1 : -1;
        Announce();
    }

    void CheckWin()
    {
        int hidden = 0;
        for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
                if (!revealed[x, y]) hidden++;

        if (hidden != mines) return;
        phase = Phase.Won;
        best++;
        ScoreStore.Save(Title, best);
    }

    // ---- input ------------------------------------------------------------

    protected override bool OnGameKey(Keys key)
    {
        // Arrows are claimed even though this is a mouse game: left unclaimed
        // they move the ToolStrip's own selection and pull focus off the board.
        switch (key)
        {
            case Keys.Left: return MoveCursor(-1, 0);
            case Keys.Right: return MoveCursor(1, 0);
            case Keys.Up: return MoveCursor(0, -1);
            case Keys.Down: return MoveCursor(0, 1);
            case Keys.Space:
            case Keys.Enter:
                if (phase != Phase.Playing) NewGame();
                else { cursorVisible = true; Reveal(cursorX, cursorY); }
                return true;
            case Keys.F:
                cursorVisible = true;
                ToggleFlag(cursorX, cursorY);
                return true;
            default:
                return false;
        }
    }

    bool MoveCursor(int dx, int dy)
    {
        cursorVisible = true;
        cursorX = Math.Clamp(cursorX + dx, 0, grid - 1);
        cursorY = Math.Clamp(cursorY + dy, 0, grid - 1);
        Announce();
        return true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase != Phase.Playing)
        {
            NewGame();
            return;
        }

        var (cell, ox, oy) = BoardLayout();
        int x = (e.X - ox) / cell, y = (e.Y - oy) / cell;
        if (x < 0 || y < 0 || x >= grid || y >= grid) return;

        cursorX = x;
        cursorY = y;

        // Shift-click flags as well as right-click: right-clicks inside a
        // ToolStrip dropdown are unusual enough to deserve a second route.
        bool flag = e.Button == MouseButtons.Right || ModifierKeys.HasFlag(Keys.Shift);
        if (flag) ToggleFlag(x, y);
        else if (e.Button == MouseButtons.Left) Reveal(x, y);
    }

    (int cell, int ox, int oy) BoardLayout()
    {
        int cell = Math.Max(6, Math.Min(ClientSize.Width / grid, ClientSize.Height / grid));
        return (cell, (ClientSize.Width - cell * grid) / 2, (ClientSize.Height - cell * grid) / 2);
    }

    // ---- painting ---------------------------------------------------------

    protected override void PaintGame(Graphics g)
    {
        var (cell, ox, oy) = BoardLayout();
        var board = new RectangleF(ox, oy, cell * grid, cell * grid);
        Draw.FillRounded(g, board, 6, Theme.BoardInner);

        for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
            {
                var r = new RectangleF(ox + x * cell + 1, oy + y * cell + 1, cell - 2, cell - 2);

                if (!revealed[x, y])
                {
                    Draw.FillRounded(g, r, 3, Theme.Tile);
                    if (flagged[x, y]) DrawFlag(g, r);
                }
                else if (mine[x, y])
                {
                    bool killer = blast.X == x && blast.Y == y;
                    Draw.FillRounded(g, r, 3, killer ? Theme.Red : Color.FromArgb(70, 40, 44));
                    using var b = new SolidBrush(killer ? Theme.BoardInner : Theme.Red);
                    float d = cell * 0.42f;
                    g.FillEllipse(b, r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
                }
                else
                {
                    Draw.FillRounded(g, r, 3, Color.FromArgb(32, 34, 39));
                    int n = Adjacent(x, y);
                    if (n > 0)
                        Draw.CenteredText(g, n.ToString(), BigFont, NumberColors[n], r);
                }

                if (cursorVisible && phase == Phase.Playing && x == cursorX && y == cursorY)
                    Draw.DrawRounded(g, r, 3, Theme.Text, 1.5f);
            }

        if (phase == Phase.Won)
            DrawOverlay(g, board, "CLEARED", $"{mines} mines found", "space for a new board", 195);
        else if (phase == Phase.Lost)
            DrawOverlay(g, board, "BOOM", "space for a new board", "", 175);
    }

    static void DrawFlag(Graphics g, RectangleF r)
    {
        float cx = r.X + r.Width * 0.38f;
        using (var pole = new Pen(Theme.TextDim, 1.4f))
            g.DrawLine(pole, cx, r.Y + r.Height * 0.22f, cx, r.Bottom - r.Height * 0.2f);

        using var flag = new SolidBrush(Theme.Red);
        g.FillPolygon(flag, new[]
        {
            new PointF(cx, r.Y + r.Height * 0.22f),
            new PointF(r.Right - r.Width * 0.18f, r.Y + r.Height * 0.36f),
            new PointF(cx, r.Y + r.Height * 0.5f),
        });
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        Draw.FillRounded(g, new RectangleF(2 * u, 2 * u, 28 * u, 28 * u), 6 * u, Theme.Tile);
        using (var pole = new Pen(Theme.Text, 2.5f * u))
            g.DrawLine(pole, 13 * u, 8 * u, 13 * u, 24 * u);
        using var flag = new SolidBrush(Theme.Red);
        g.FillPolygon(flag, new[]
        {
            new PointF(13 * u, 8 * u),
            new PointF(25 * u, 13 * u),
            new PointF(13 * u, 18 * u),
        });
    }

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        var (cell, ox, oy) = BoardLayout();
        Point At(int x, int y) => new(ox + x * cell + cell / 2, oy + y * cell + cell / 2);

        var c = At(4, 4);
        await ctx.Click(c.X, c.Y);
        ctx.Log($"after reveal  : phase={phase} minesPlaced={minesPlaced} revealedCentre={revealed[4, 4]}");
        ctx.Snap("1-revealed");

        var f = At(0, 0);
        await ctx.Click(f.X, f.Y, right: true);
        ctx.Log($"after R-click : flags={flags} flagged(0,0)={flagged[0, 0]}");

        var f2 = At(8, 0);
        await ctx.Tap(Keys.Right, 60);
        Cursor.Position = PointToScreen(new Point(f2.X, f2.Y));
        await ctx.Delay(60);
        ctx.Log($"keyboard flag : moving cursor with arrows then F");
        for (int i = 0; i < 4; i++) await ctx.Tap(Keys.Up, 60);
        await ctx.Tap(Keys.F, 200);
        ctx.Log($"after F       : flags={flags}");
        ctx.Snap("2-flagged");

        ctx.Log($"cursor        : ({cursorX},{cursorY})");
    }
}
