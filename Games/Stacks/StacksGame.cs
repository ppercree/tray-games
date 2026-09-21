using TrayGames.Core;

namespace TrayGames.Stacks;

internal enum Phase { Ready, Playing, Dead }

internal struct Block
{
    public float X;
    public float W;
    public Color Color;
}

internal struct Shard
{
    public float X, W, Y, VelY;
    public Color Color;
}

internal sealed class StacksGame : GameControl
{
    const int BoardW = 180;
    const int BoardH = 200;
    const float BlockH = 14f;
    float startWidth = 110f;
    float baseSpeed = 1.5f;
    float speedCap = 4.6f;
    float perfectTolerance = 1.6f;

    readonly Setting speedSetting;
    readonly Setting widthSetting;
    readonly Setting snapSetting;
    const int VisibleRows = 12;

    static readonly Color[] Palette =
    {
        Theme.Cyan, Theme.Blue, Theme.Purple, Theme.Red, Theme.Orange, Theme.Yellow, Theme.Green,
    };

    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly List<Block> tower = new();
    readonly List<Shard> shards = new();

    Phase phase;
    Block moving;
    float direction = 1f;
    int score, best, perfects;
    float flash;

    public StacksGame() : base(BoardW, BoardH)
    {
        best = ScoreStore.Load(Title);
        speedSetting = AddSetting("speed", "Start speed",
            new[] { "Slow", "Normal", "Fast", "Blistering" }, 1);
        widthSetting = AddSetting("width", "Start width", new[] { "Wide", "Normal", "Narrow" }, 1);
        snapSetting = AddSetting("snap", "Perfect snap", new[] { "Forgiving", "Normal", "Strict" }, 1);
        timer.Tick += (_, _) => Tick();
        ApplySettings();
    }

    protected override void ApplySettings()
    {
        (baseSpeed, speedCap) = speedSetting.Index switch
        {
            0 => (0.9f, 3.2f),
            2 => (2.3f, 6.0f),
            3 => (3.2f, 7.5f),
            _ => (1.5f, 4.6f),
        };
        startWidth = widthSetting.Index switch { 0 => 140f, 2 => 80f, _ => 110f };
        perfectTolerance = snapSetting.Index switch { 0 => 3.2f, 2 => 0.7f, _ => 1.6f };
        NewGame();
    }

    public override string Title => "Stacks";
    public override string Status => $"height {score}   perfect {perfects}   best {best}";

    float Speed => Math.Min(speedCap, baseSpeed + score * 0.075f);

    public override void NewGame()
    {
        timer.Stop();
        tower.Clear();
        shards.Clear();
        score = 0;
        perfects = 0;
        flash = 0;
        phase = Phase.Ready;

        tower.Add(new Block { X = (BoardW - startWidth) / 2f, W = startWidth, Color = Palette[0] });
        SpawnMoving();
        Announce();
    }

    public override void SuspendForMenu()
    {
        if (phase != Phase.Playing) return;
        phase = Phase.Ready;
        timer.Stop();
        Announce();
    }

    void SpawnMoving()
    {
        var below = tower[^1];
        moving = new Block
        {
            X = direction > 0 ? 0 : BoardW - below.W,
            W = below.W,
            Color = Palette[tower.Count % Palette.Length],
        };
    }

    void Start()
    {
        if (phase == Phase.Dead) { NewGame(); return; }
        phase = Phase.Playing;
        timer.Start();
        Announce();
    }

    void Tick()
    {
        if (phase == Phase.Playing)
        {
            moving.X += direction * Speed;
            if (moving.X <= 0) { moving.X = 0; direction = 1f; }
            else if (moving.X + moving.W >= BoardW) { moving.X = BoardW - moving.W; direction = -1f; }
        }

        for (int i = shards.Count - 1; i >= 0; i--)
        {
            var s = shards[i];
            s.VelY += 0.9f;
            s.Y += s.VelY;
            shards[i] = s;
            if (s.Y > BoardH + 80) shards.RemoveAt(i);
        }

        if (flash > 0) flash = Math.Max(0, flash - 0.06f);

        Invalidate();
        if (phase != Phase.Playing && shards.Count == 0 && flash <= 0) timer.Stop();
    }

    void Place()
    {
        if (phase != Phase.Playing) return;

        var below = tower[^1];
        float left = Math.Max(moving.X, below.X);
        float right = Math.Min(moving.X + moving.W, below.X + below.W);
        float overlap = right - left;

        if (overlap <= 0)
        {
            // Nothing landed on the tower: the whole block falls away.
            shards.Add(new Shard { X = moving.X, W = moving.W, Y = 0, VelY = 0, Color = moving.Color });
            phase = Phase.Dead;
            if (score > best) { best = score; ScoreStore.Save(Title, best); }
            Announce();
            return;
        }

        bool perfect = Math.Abs(moving.X - below.X) <= perfectTolerance;
        if (perfect)
        {
            // Snapping a near-perfect drop keeps the tower alive far longer than
            // it deserves, which is exactly what makes the game feel good.
            left = below.X;
            overlap = below.W;
            perfects++;
            flash = 1f;
        }
        else
        {
            float trimmedW = moving.W - overlap;
            if (trimmedW > 0.5f)
            {
                float trimmedX = moving.X < below.X ? moving.X : right;
                shards.Add(new Shard { X = trimmedX, W = trimmedW, Y = 0, VelY = 0, Color = moving.Color });
            }
        }

        tower.Add(new Block { X = left, W = overlap, Color = moving.Color });
        score++;
        SpawnMoving();
        Announce();
    }

    protected override bool OnGameKey(Keys key)
    {
        switch (key)
        {
            case Keys.Space:
            case Keys.Enter:
            case Keys.Down:
            case Keys.S:
                if (phase == Phase.Playing) Place();
                else Start();
                return true;
            case Keys.Up:
            case Keys.Left:
            case Keys.Right:
                if (phase != Phase.Playing) Start();
                return true;
            default:
                return false;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase == Phase.Playing) Place();
        else Start();
    }

    // ---- painting ---------------------------------------------------------

    /// The tower scrolls once it is taller than the window, so the active block
    /// stays put instead of climbing out of view.
    float CameraOffset() => Math.Max(0, tower.Count - (VisibleRows - 2)) * BlockH;

    protected override void PaintGame(Graphics g)
    {
        var board = new RectangleF(0, 0, BoardW, BoardH);
        Draw.FillRounded(g, board, 6, Theme.BoardInner);

        float camera = CameraOffset();
        float groundY = BoardH - 8;

        for (int i = 0; i < tower.Count; i++)
        {
            float y = groundY - (i + 1) * BlockH + camera;
            if (y > BoardH || y + BlockH < 0) continue;
            DrawBlock(g, tower[i].X, y, tower[i].W, tower[i].Color);
        }

        if (phase == Phase.Playing)
        {
            float y = groundY - (tower.Count + 1) * BlockH + camera;
            DrawBlock(g, moving.X, y, moving.W, moving.Color);
        }

        foreach (var s in shards)
        {
            float y = groundY - (tower.Count + 1) * BlockH + camera + s.Y;
            DrawBlock(g, s.X, y, s.W, Color.FromArgb(150, s.Color));
        }

        if (flash > 0)
        {
            using var brush = new SolidBrush(Color.FromArgb((int)(90 * flash), Theme.Text));
            g.FillRectangle(brush, board);
        }

        if (phase == Phase.Ready)
            DrawOverlay(g, board, "STACKS", "space or click to drop", "", 170);
        else if (phase == Phase.Dead)
            DrawOverlay(g, board, "MISSED", $"height {score}   perfect {perfects}", "space for a new tower", 200);
    }

    static void DrawBlock(Graphics g, float x, float y, float w, Color c)
    {
        if (w <= 0) return;
        var r = new RectangleF(x, y, w, BlockH - 1.5f);
        Draw.FillRounded(g, r, 2.5f, c);
        Draw.FillRounded(g, new RectangleF(r.X + 2, r.Y + 1.5f, Math.Max(0, r.Width - 4), r.Height / 3f), 1.5f,
            Color.FromArgb(55, 255, 255, 255));
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        void B(float x, float y, float w, Color c) =>
            Draw.FillRounded(g, new RectangleF(x * u, y * u, w * u, 6 * u), 1.5f * u, c);

        B(3, 24, 26, Theme.Cyan);
        B(5, 17, 22, Theme.Blue);
        B(8, 10, 17, Theme.Purple);
        B(11, 3, 12, Theme.Red);
    }

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        await ctx.Tap(Keys.Space, 300);
        ctx.Log($"after start   : phase={phase} movingW={moving.W:0.0}");

        // Drop whenever the moving block overlaps the one below: a blind timer
        // would only ever prove that the key is wired up.
        for (int i = 0; i < 40 && phase == Phase.Playing; i++)
        {
            var below = tower[^1];
            for (int guard = 0; guard < 400; guard++)
            {
                float left = Math.Max(moving.X, below.X);
                float right = Math.Min(moving.X + moving.W, below.X + below.W);
                if (right - left > moving.W * 0.75f) break;
                await ctx.Delay(8);
            }
            await ctx.Tap(Keys.Space, 60);
            if (i == 6) ctx.Snap("1-tower");
        }

        ctx.Log($"after drops   : phase={phase} height={score} perfects={perfects} topW={tower[^1].W:0.0}");
        ctx.Log($"widths        : {string.Join(",", tower.Select(b => b.W.ToString("0")))}");
        ctx.Snap("2-end");
    }
}
