using TrayGames.Core;

namespace TrayGames.Tetris;

internal enum Phase { Ready, Playing, Paused, Dead }

/// A tetromino as cells inside a box of `Box` squares. Rotations are derived
/// rather than tabulated: clockwise in a box of size n is (x,y) -> (n-1-y, x).
internal sealed class Piece
{
    public required Point[] Cells { get; init; }
    public required int Box { get; init; }
    public required Color Color { get; init; }

    public Point[] Rotated(int turns)
    {
        var cells = Cells;
        for (int t = 0; t < ((turns % 4) + 4) % 4; t++)
            cells = cells.Select(c => new Point(Box - 1 - c.Y, c.X)).ToArray();
        return cells;
    }
}

internal sealed class TetrisGame : GameControl
{
    const int Rows = 18;
    const int CellPx = 11;
    const int PanelPx = 56;

    int cols = 10;
    int startLevel;
    bool showGhost = true;

    readonly Setting levelSetting;
    readonly Setting widthSetting;
    readonly Setting ghostSetting;

    static readonly Piece[] Pieces =
    {
        new() { Box = 4, Color = Theme.Cyan,   Cells = new[] { new Point(0, 1), new(1, 1), new(2, 1), new(3, 1) } }, // I
        new() { Box = 3, Color = Theme.Blue,   Cells = new[] { new Point(0, 0), new(0, 1), new(1, 1), new(2, 1) } }, // J
        new() { Box = 3, Color = Theme.Orange, Cells = new[] { new Point(2, 0), new(0, 1), new(1, 1), new(2, 1) } }, // L
        new() { Box = 2, Color = Theme.Yellow, Cells = new[] { new Point(0, 0), new(1, 0), new(0, 1), new(1, 1) } }, // O
        new() { Box = 3, Color = Theme.Green,  Cells = new[] { new Point(1, 0), new(2, 0), new(0, 1), new(1, 1) } }, // S
        new() { Box = 3, Color = Theme.Purple, Cells = new[] { new Point(1, 0), new(0, 1), new(1, 1), new(2, 1) } }, // T
        new() { Box = 3, Color = Theme.Red,    Cells = new[] { new Point(0, 0), new(1, 0), new(1, 1), new(2, 1) } }, // Z
    };

    static readonly int[] LineScores = { 0, 40, 100, 300, 1200 };

    readonly System.Windows.Forms.Timer timer = new();
    Color?[,] well = new Color?[10, Rows];
    readonly Random rng = new();

    Piece piece = Pieces[0];
    Piece nextPiece = Pieces[0];
    int rotation;
    int px, py;
    Phase phase;
    int score, lines, best;

    public TetrisGame() : base(10 * CellPx + PanelPx, Rows * CellPx)
    {
        best = ScoreStore.Load(Title);
        levelSetting = AddSetting("level", "Start level", new[] { "1", "4", "7", "10" }, 1);
        widthSetting = AddSetting("width", "Well", new[] { "Narrow", "Normal", "Wide" }, 1);
        ghostSetting = AddSetting("ghost", "Ghost piece", new[] { "On", "Off" }, 0);
        timer.Tick += (_, _) => Fall();
        ApplySettings();
    }

    protected override void ApplySettings()
    {
        cols = widthSetting.Index switch { 0 => 8, 2 => 12, _ => 10 };
        startLevel = levelSetting.Index switch { 1 => 3, 2 => 6, 3 => 9, _ => 0 };
        showGhost = ghostSetting.Index == 0;

        well = new Color?[cols, Rows];
        SetBoardSize(cols * CellPx + PanelPx, Rows * CellPx);
        NewGame();
    }

    public override string Title => "Tetris";
    public override string Status => $"score {score}   lines {lines}   best {best}";

    int Level => startLevel + lines / 5;

    // Steeper than the classic curve, because the whole point of a start level
    // is that level 10 should already be uncomfortable.
    int Interval => Math.Max(70, 520 - Level * 45);

    public override void NewGame()
    {
        timer.Stop();
        Array.Clear(well);
        score = 0;
        lines = 0;
        phase = Phase.Ready;
        nextPiece = Pieces[rng.Next(Pieces.Length)];
        Spawn();
        Announce();
    }

    public override void SuspendForMenu()
    {
        if (phase != Phase.Playing) return;
        phase = Phase.Paused;
        timer.Stop();
        Announce();
    }

    void Start()
    {
        phase = Phase.Playing;
        timer.Interval = Interval;
        timer.Start();
        Announce();
    }

    void TogglePause()
    {
        switch (phase)
        {
            case Phase.Ready:
            case Phase.Paused: Start(); break;
            case Phase.Playing: phase = Phase.Paused; timer.Stop(); Announce(); break;
            case Phase.Dead: NewGame(); break;
        }
    }

    void Spawn()
    {
        piece = nextPiece;
        nextPiece = Pieces[rng.Next(Pieces.Length)];
        rotation = 0;
        px = (cols - piece.Box) / 2;
        py = piece.Box == 4 ? -1 : 0;

        if (Collides(px, py, rotation))
        {
            phase = Phase.Dead;
            timer.Stop();
            if (score > best) { best = score; ScoreStore.Save(Title, best); }
            Announce();
        }
    }

    bool Collides(int atX, int atY, int rot)
    {
        foreach (var c in piece.Rotated(rot))
        {
            int x = atX + c.X, y = atY + c.Y;
            if (x < 0 || x >= cols || y >= Rows) return true;
            if (y >= 0 && well[x, y] is not null) return true;
        }
        return false;
    }

    void Fall()
    {
        if (phase != Phase.Playing) return;
        if (!Collides(px, py + 1, rotation)) { py++; Invalidate(); return; }
        Lock();
    }

    void Lock()
    {
        foreach (var c in piece.Rotated(rotation))
        {
            int x = px + c.X, y = py + c.Y;
            if (y >= 0 && y < Rows && x >= 0 && x < cols) well[x, y] = piece.Color;
        }

        int cleared = ClearLines();
        if (cleared > 0)
        {
            lines += cleared;
            score += LineScores[cleared] * (Level + 1);
            timer.Interval = Interval;
        }

        Spawn();
        Announce();
    }

    int ClearLines()
    {
        int cleared = 0;
        for (int y = Rows - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < cols; x++)
                if (well[x, y] is null) { full = false; break; }
            if (!full) continue;

            for (int yy = y; yy > 0; yy--)
                for (int x = 0; x < cols; x++)
                    well[x, yy] = well[x, yy - 1];
            for (int x = 0; x < cols; x++) well[x, 0] = null;

            cleared++;
            y++; // the row that dropped into y has not been checked yet
        }
        return cleared;
    }

    void Rotate()
    {
        int next = (rotation + 1) % 4;
        // Wall kicks, nearest offset first: without them a piece against a wall
        // simply refuses to turn, which reads as an unresponsive control.
        foreach (int kick in new[] { 0, -1, 1, -2, 2 })
            if (!Collides(px + kick, py, next))
            {
                px += kick;
                rotation = next;
                Invalidate();
                return;
            }
    }

    void HardDrop()
    {
        while (!Collides(px, py + 1, rotation)) { py++; score += 1; }
        Lock();
    }

    int GhostY()
    {
        int y = py;
        while (!Collides(px, y + 1, rotation)) y++;
        return y;
    }

    protected override bool OnGameKey(Keys key)
    {
        if (phase is Phase.Ready or Phase.Paused or Phase.Dead)
        {
            if (key is Keys.Space or Keys.Enter or Keys.Up or Keys.Left or Keys.Right or Keys.Down)
            {
                TogglePause();
                return true;
            }
            return false;
        }

        switch (key)
        {
            case Keys.Left:
            case Keys.Q:
            case Keys.A:
                if (!Collides(px - 1, py, rotation)) { px--; Invalidate(); }
                return true;
            case Keys.Right:
            case Keys.D:
                if (!Collides(px + 1, py, rotation)) { px++; Invalidate(); }
                return true;
            case Keys.Up:
            case Keys.Z:
            case Keys.W:
                Rotate();
                return true;
            case Keys.Down:
            case Keys.S:
                if (!Collides(px, py + 1, rotation)) { py++; score++; Announce(); }
                return true;
            case Keys.Space:
                HardDrop();
                return true;
            case Keys.P:
                TogglePause();
                return true;
            default:
                return false;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase != Phase.Playing) { TogglePause(); return; }

        // Clicking a column walks the piece toward it, which makes the game
        // usable one-handed with the mouse already on the menu.
        int col = e.X / CellPx;
        if (col < px && !Collides(px - 1, py, rotation)) px--;
        else if (col > px && !Collides(px + 1, py, rotation)) px++;
        else Rotate();
        Invalidate();
    }

    protected override void PaintGame(Graphics g)
    {
        var field = new RectangleF(0, 0, cols * CellPx, Rows * CellPx);
        Draw.FillRounded(g, field, 5, Theme.BoardInner);

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < cols; x++)
                if (well[x, y] is Color c) Block(g, x, y, c);

        if (phase != Phase.Dead)
        {
            int ghost = showGhost ? GhostY() : py;
            if (ghost != py)
                foreach (var c in piece.Rotated(rotation))
                    if (ghost + c.Y >= 0)
                        Draw.DrawRounded(g,
                            new RectangleF((px + c.X) * CellPx + 1.5f, (ghost + c.Y) * CellPx + 1.5f, CellPx - 3, CellPx - 3),
                            2, Color.FromArgb(60, piece.Color));

            foreach (var c in piece.Rotated(rotation))
                if (py + c.Y >= 0) Block(g, px + c.X, py + c.Y, piece.Color);
        }

        DrawPanel(g);

        switch (phase)
        {
            case Phase.Ready:
                DrawOverlay(g, field, "TETRIS", "any arrow to start", "", 170);
                break;
            case Phase.Paused:
                DrawOverlay(g, field, "PAUSED", "space to resume", "", 170);
                break;
            case Phase.Dead:
                DrawOverlay(g, field, "TOPPED OUT", $"score {score}", "space for a new game", 200);
                break;
        }
    }

    static void Block(Graphics g, int x, int y, Color c)
    {
        var r = new RectangleF(x * CellPx + 1, y * CellPx + 1, CellPx - 2, CellPx - 2);
        Draw.FillRounded(g, r, 2.5f, c);
        Draw.FillRounded(g, new RectangleF(r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height / 2.6f), 1.5f,
            Color.FromArgb(55, 255, 255, 255));
    }

    void DrawPanel(Graphics g)
    {
        float x0 = cols * CellPx + 6;
        Draw.CenteredText(g, "NEXT", TinyFont, Theme.TextDim, new RectangleF(x0, 6, PanelPx - 12, 12));

        var box = new RectangleF(x0, 20, PanelPx - 12, PanelPx - 12);
        Draw.FillRounded(g, box, 4, Theme.BoardInner);

        var cells = nextPiece.Cells;
        float minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X);
        float minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        float w = (maxX - minX + 1) * 9, h = (maxY - minY + 1) * 9;
        float ox = box.X + (box.Width - w) / 2, oy = box.Y + (box.Height - h) / 2;

        foreach (var c in cells)
            Draw.FillRounded(g,
                new RectangleF(ox + (c.X - minX) * 9 + 0.5f, oy + (c.Y - minY) * 9 + 0.5f, 8, 8),
                2, nextPiece.Color);

        Draw.CenteredText(g, "LEVEL", TinyFont, Theme.TextDim, new RectangleF(x0, box.Bottom + 10, PanelPx - 12, 12));
        Draw.CenteredText(g, (Level + 1).ToString(), BigFont, Theme.Text, new RectangleF(x0, box.Bottom + 22, PanelPx - 12, 18));
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        void B(float x, float y, Color c) =>
            Draw.FillRounded(g, new RectangleF(x * u, y * u, 9 * u, 9 * u), 2 * u, c);

        B(2, 12, Theme.Purple);
        B(12, 12, Theme.Purple);
        B(22, 12, Theme.Purple);
        B(12, 2, Theme.Purple);
        B(2, 22, Theme.Cyan);
        B(12, 22, Theme.Cyan);
        B(22, 22, Theme.Cyan);
    }

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        await ctx.Tap(Keys.Left, 400);
        ctx.Log($"after start   : phase={phase} piece at ({px},{py})");

        // Gravity keeps running between injected keystrokes, so the driver
        // cannot be precise. It only has to show that moving, rotating and
        // locking all work together under a live clock.
        for (int i = 0; i < 14 && phase == Phase.Playing; i++)
        {
            int target = LowestColumn();
            for (int guard = 0; guard < 8 && px != target && phase == Phase.Playing; guard++)
                await ctx.Tap(px > target ? Keys.Left : Keys.Right, 35);
            if (phase != Phase.Playing) break;
            await ctx.Tap(Keys.Space, 170);
        }

        int filled = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < cols; x++)
                if (well[x, y] is not null) filled++;
        ctx.Log($"after drops   : phase={phase} score={score} cellsLocked={filled}");
        ctx.Snap("1-stacked");

        // Line clearing needs a full row, which a blind driver will not build in
        // any reasonable time. Seeding the row keeps the trigger honest: the
        // clear still has to happen via space -> HardDrop -> Lock -> ClearLines.
        if (phase != Phase.Playing) { NewGame(); await ctx.Tap(Keys.Left, 300); }

        int linesBefore = lines;
        for (int x = 0; x < cols; x++) well[x, Rows - 1] = Theme.Blue;
        Invalidate();
        await ctx.Delay(150);
        ctx.Log($"seeded row    : bottom row full, lines={lines}");

        await ctx.Tap(Keys.Space, 350);
        int bottomFilled = 0;
        for (int x = 0; x < cols; x++) if (well[x, Rows - 1] is not null) bottomFilled++;
        ctx.Log($"after drop    : lines={lines} (was {linesBefore}) bottomRowCells={bottomFilled}/{cols}");
        ctx.Snap("2-cleared");
    }

    int LowestColumn()
    {
        int bestCol = 0, bestHeight = int.MaxValue;
        for (int x = 0; x < cols; x++)
        {
            int h = 0;
            while (h < Rows && well[x, Rows - 1 - h] is not null) h++;
            if (h < bestHeight) { bestHeight = h; bestCol = x; }
        }
        return bestCol;
    }
}
