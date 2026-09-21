using System.Drawing.Drawing2D;
using TrayGames.Core;

namespace TrayGames.Snake;

internal enum Phase { Ready, Playing, Paused, Dead }

internal readonly record struct Cell(int X, int Y);

internal sealed class SnakeGame : GameControl
{
    int cols = 16;
    int rows = 16;
    int cellPx = 12;
    int startInterval = 140;
    int minInterval = 70;
    bool wrapWalls;

    readonly Setting speedSetting;
    readonly Setting boardSetting;
    readonly Setting wallSetting;

    readonly System.Windows.Forms.Timer timer = new();
    readonly List<Cell> body = new();
    readonly Random rng = new();

    Cell dir, queuedDir, food;
    Phase phase;
    int score;
    int best;

    public SnakeGame() : base(192, 192)
    {
        best = ScoreStore.Load(Title);
        speedSetting = AddSetting("speed", "Speed", new[] { "Calm", "Normal", "Brisk", "Frantic" }, 1);
        boardSetting = AddSetting("board", "Board", new[] { "Small", "Medium", "Large" }, 1);
        wallSetting = AddSetting("walls", "Walls", new[] { "Solid", "Wrap" }, 0);
        timer.Tick += (_, _) => Step();
        ApplySettings();
    }

    protected override void ApplySettings()
    {
        (cols, rows, cellPx) = boardSetting.Index switch
        {
            0 => (12, 12, 16),
            2 => (20, 20, 10),
            _ => (16, 16, 12),
        };
        (startInterval, minInterval) = speedSetting.Index switch
        {
            0 => (190, 110),
            2 => (105, 55),
            3 => (80, 42),
            _ => (140, 70),
        };
        wrapWalls = wallSetting.Index == 1;

        SetBoardSize(cols * cellPx, rows * cellPx);
        NewGame();
    }

    public override string Title => "Snake";
    public override string Status => $"score {score}      best {best}";

    public override void NewGame()
    {
        timer.Stop();
        timer.Interval = startInterval;
        body.Clear();
        int cx = cols / 2, cy = rows / 2;
        body.Add(new Cell(cx, cy));
        body.Add(new Cell(cx - 1, cy));
        body.Add(new Cell(cx - 2, cy));
        dir = queuedDir = new Cell(1, 0);
        score = 0;
        phase = Phase.Ready;
        PlaceFood();
        Announce();
    }

    public override void SuspendForMenu()
    {
        if (phase != Phase.Playing) return;
        phase = Phase.Paused;
        timer.Stop();
        Announce();
    }

    void TogglePause()
    {
        switch (phase)
        {
            case Phase.Ready:
            case Phase.Paused:
                phase = Phase.Playing;
                timer.Start();
                break;
            case Phase.Playing:
                phase = Phase.Paused;
                timer.Stop();
                break;
            case Phase.Dead:
                NewGame();
                return;
        }
        Announce();
    }

    void Step()
    {
        if (phase != Phase.Playing) return;

        dir = queuedDir;
        var head = new Cell(body[0].X + dir.X, body[0].Y + dir.Y);

        if (wrapWalls)
            head = new Cell((head.X + cols) % cols, (head.Y + rows) % rows);
        else if (head.X < 0 || head.Y < 0 || head.X >= cols || head.Y >= rows)
        {
            Die();
            return;
        }

        bool eating = head == food;

        // The tail cell empties this same tick, so moving into it is legal -
        // unless the snake is growing, in which case the tail stays put.
        int checkTo = eating ? body.Count : body.Count - 1;
        for (int i = 0; i < checkTo; i++)
            if (body[i] == head) { Die(); return; }

        body.Insert(0, head);
        if (eating)
        {
            score++;
            if (score > best) { best = score; ScoreStore.Save(Title, best); }
            timer.Interval = Math.Max(minInterval, startInterval - score * 3);
            PlaceFood();
            Announce();
        }
        else
        {
            body.RemoveAt(body.Count - 1);
        }

        Invalidate();
    }

    void Die()
    {
        timer.Stop();
        phase = Phase.Dead;
        Announce();
    }

    void PlaceFood()
    {
        var free = new List<Cell>(cols * rows);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var c = new Cell(x, y);
                if (!body.Contains(c)) free.Add(c);
            }

        if (free.Count == 0) { timer.Stop(); phase = Phase.Dead; return; }
        food = free[rng.Next(free.Count)];
    }

    protected override bool OnGameKey(Keys key)
    {
        // Keys carries the layout-mapped virtual key, so ZQSD lands where an
        // AZERTY user expects it and WASD where a QWERTY user does.
        switch (key)
        {
            case Keys.Up:
            case Keys.W:
            case Keys.Z: return Turn(0, -1);
            case Keys.Down:
            case Keys.S: return Turn(0, 1);
            case Keys.Left:
            case Keys.A:
            case Keys.Q: return Turn(-1, 0);
            case Keys.Right:
            case Keys.D: return Turn(1, 0);
            case Keys.Space:
            case Keys.Enter: TogglePause(); return true;
            default: return false;
        }
    }

    bool Turn(int dx, int dy)
    {
        if (phase == Phase.Ready)
        {
            phase = Phase.Playing;
            timer.Start();
            Announce();
        }
        else if (phase != Phase.Playing)
        {
            return true;
        }

        // Compare against the applied direction, not the queued one: two turns
        // inside a single tick could otherwise fold the snake into its neck.
        if (dx == -dir.X && dy == -dir.Y) return true;

        queuedDir = new Cell(dx, dy);
        return true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase is Phase.Dead or Phase.Paused or Phase.Ready) { TogglePause(); return; }

        var (cell, ox, oy) = BoardLayout();
        float hx = ox + (body[0].X + 0.5f) * cell;
        float hy = oy + (body[0].Y + 0.5f) * cell;
        float dx = e.X - hx, dy = e.Y - hy;
        if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2) { TogglePause(); return; }

        if (Math.Abs(dx) > Math.Abs(dy)) Turn(Math.Sign(dx), 0);
        else Turn(0, Math.Sign(dy));
    }

    (int cell, int ox, int oy) BoardLayout()
    {
        int cell = Math.Max(4, Math.Min(ClientSize.Width / cols, ClientSize.Height / rows));
        return (cell, (ClientSize.Width - cell * cols) / 2, (ClientSize.Height - cell * rows) / 2);
    }

    protected override void PaintGame(Graphics g)
    {
        var (cell, ox, oy) = BoardLayout();
        var board = new RectangleF(ox, oy, cell * cols, cell * rows);
        Draw.FillRounded(g, board, 6, Theme.BoardInner);

        float fr = cell * 0.62f;
        using (var fb = new SolidBrush(Theme.Red))
            g.FillEllipse(fb, ox + (food.X + 0.5f) * cell - fr / 2, oy + (food.Y + 0.5f) * cell - fr / 2, fr, fr);

        DrawSnake(g, cell, ox, oy);

        switch (phase)
        {
            case Phase.Ready:
                DrawOverlay(g, board, "SNAKE", "arrows or ZQSD to start", "", 170);
                break;
            case Phase.Paused:
                DrawOverlay(g, board, "PAUSED", "space to resume", "", 170);
                break;
            case Phase.Dead:
                DrawOverlay(g, board, "GAME OVER", $"score {score}   best {best}", "space for a new game", 200);
                break;
        }
    }

    // Per-cell squares read as a dotted line at this size, so the body is drawn
    // as round-capped segments between cell centres: the caps close the gaps and
    // round off the corners for free, while each segment keeps its own shade.
    void DrawSnake(Graphics g, int cell, int ox, int oy)
    {
        PointF Centre(Cell c) => new(ox + (c.X + 0.5f) * cell, oy + (c.Y + 0.5f) * cell);
        float width = Math.Max(3f, cell - 2f);

        for (int i = body.Count - 1; i > 0; i--)
        {
            var shade = Theme.Blend(Theme.GreenDark, Theme.Green, 1f - (float)i / body.Count);
            using var pen = new Pen(shade, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            // In wrap mode consecutive cells can sit on opposite edges; joining
            // them would draw a stripe straight across the board.
            bool wrapped = Math.Abs(body[i].X - body[i - 1].X) > 1
                        || Math.Abs(body[i].Y - body[i - 1].Y) > 1;
            if (wrapped)
            {
                var p = Centre(body[i]);
                using var cap = new SolidBrush(shade);
                g.FillEllipse(cap, p.X - width / 2, p.Y - width / 2, width, width);
                continue;
            }

            g.DrawLine(pen, Centre(body[i]), Centre(body[i - 1]));
        }

        var head = Centre(body[0]);
        using var hb = new SolidBrush(Theme.Green);
        g.FillEllipse(hb, head.X - width / 2, head.Y - width / 2, width, width);
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        using var pen = new Pen(Theme.Green, 8 * u)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new[]
        {
            new PointF(6 * u, 25 * u),
            new PointF(20 * u, 25 * u),
            new PointF(20 * u, 14 * u),
        });
        using var food = new SolidBrush(Theme.Red);
        g.FillEllipse(food, 4 * u, 4 * u, 10 * u, 10 * u);
    }

    internal Cell Head => body[0];
    internal Cell Food => food;
    internal Cell Direction => dir;
    internal int Score => score;
    internal bool Alive => phase == Phase.Playing;

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        await ctx.Tap(Keys.Right, 500);
        ctx.Log($"after RIGHT   : phase={phase} head=({Head.X},{Head.Y})");
        await ctx.Tap(Keys.Down, 500);
        ctx.Log($"after DOWN    : phase={phase} head=({Head.X},{Head.Y}) dir=({dir.X},{dir.Y})");

        // Chase food with a wall-aware greedy driver: the only way to exercise
        // growth and the high-score write through the real input path.
        bool snapped = false;
        for (int step = 0; step < 120 && phase == Phase.Playing; step++)
        {
            if (!snapped && score >= 4) { ctx.Snap("1-playing"); snapped = true; }

            int dx = food.X - Head.X, dy = food.Y - Head.Y;
            var wanted = new List<(Keys key, int x, int y)>();
            var h = (key: dx > 0 ? Keys.Right : Keys.Left, x: Math.Sign(dx), y: 0);
            var v = (key: dy > 0 ? Keys.Down : Keys.Up, x: 0, y: Math.Sign(dy));
            if (Math.Abs(dx) >= Math.Abs(dy)) { wanted.Add(h); wanted.Add(v); }
            else { wanted.Add(v); wanted.Add(h); }
            wanted.Add((Keys.Up, 0, -1));
            wanted.Add((Keys.Down, 0, 1));
            wanted.Add((Keys.Left, -1, 0));
            wanted.Add((Keys.Right, 1, 0));

            foreach (var (key, x, y) in wanted)
            {
                if (x == 0 && y == 0) continue;
                if (x == -dir.X && y == -dir.Y) continue;
                int nx = Head.X + x, ny = Head.Y + y;
                if (!wrapWalls && (nx < 0 || ny < 0 || nx >= cols || ny >= rows)) continue;
                if (x != dir.X || y != dir.Y) ctx.Press(key);
                break;
            }

            await ctx.Delay(80);
        }

        ctx.Log($"after chase   : phase={phase} score={score} best={best}");
        ctx.Snap("2-end");
    }
}
