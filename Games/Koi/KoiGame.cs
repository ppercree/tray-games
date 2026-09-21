using System.Drawing.Drawing2D;
using TrayGames.Core;

namespace TrayGames.Koi;

internal sealed class Fish
{
    public PointF Pos;
    public float Heading;
    public float Speed;
    public float Size;
    public Color Body;
    public Color Patch;
    public float WanderPhase;
    public int Fed;
}

internal sealed class Pellet
{
    public PointF Pos;
    public float Life = 1f;
    public float Sink;
}

internal sealed class Ripple
{
    public PointF Pos;
    public float Radius;
    public float Life = 1f;
}

/// A toy rather than a game: there is nothing to lose, only koi to feed. Fish
/// steer toward the nearest pellet and multiply as they are fed.
internal sealed class KoiGame : GameControl
{
    const int PondW = 192;
    const int PondH = 192;
    const int FeedsPerKoi = 5;

    int startingKoi = 3;
    int maxKoi = 9;
    float liveliness = 1f;

    readonly Setting startSetting;
    readonly Setting limitSetting;
    readonly Setting paceSetting;

    static readonly (Color body, Color patch)[] Patterns =
    {
        (Color.FromArgb(244, 246, 248), Color.FromArgb(232, 120, 62)),
        (Color.FromArgb(240, 156, 74), Color.FromArgb(248, 250, 252)),
        (Color.FromArgb(246, 248, 250), Color.FromArgb(52, 56, 66)),
        (Color.FromArgb(232, 196, 88), Color.FromArgb(226, 106, 74)),
        (Color.FromArgb(226, 110, 84), Color.FromArgb(250, 250, 250)),
    };

    readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    readonly List<Fish> koi = new();
    readonly List<Pellet> pellets = new();
    readonly List<Ripple> ripples = new();
    readonly Random rng = new();

    int totalFed, best;
    int pendingKoi;
    float clock;

    public KoiGame() : base(PondW, PondH)
    {
        best = ScoreStore.Load(Title);
        startSetting = AddSetting("starting", "Starting koi", new[] { "1", "3", "5", "8" }, 1);
        limitSetting = AddSetting("limit", "Pond holds", new[] { "6", "9", "12" }, 1);
        paceSetting = AddSetting("pace", "Liveliness", new[] { "Lazy", "Calm", "Lively" }, 1);
        timer.Tick += (_, _) => Tick();
        ApplySettings();
        timer.Start();
    }

    protected override void ApplySettings()
    {
        startingKoi = startSetting.Index switch { 0 => 1, 2 => 5, 3 => 8, _ => 3 };
        maxKoi = limitSetting.Index switch { 0 => 6, 2 => 12, _ => 9 };
        liveliness = paceSetting.Index switch { 0 => 0.55f, 2 => 1.6f, _ => 1f };
        NewGame();
    }

    public override string Title => "Feed the Koi";
    public override string Status => $"koi {koi.Count}   fed {totalFed}   best {best}";

    public override void NewGame()
    {
        koi.Clear();
        pellets.Clear();
        ripples.Clear();
        totalFed = 0;
        pendingKoi = 0;
        for (int i = 0; i < startingKoi; i++) AddKoi();
        timer.Start();
        Announce();
    }

    // The pond is only alive while it is on screen; a tray toy that keeps a
    // 30fps timer running in the background would be rude.
    public override void SuspendForMenu() => timer.Stop();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        timer.Start();
    }

    void AddKoi()
    {
        var (body, patch) = Patterns[rng.Next(Patterns.Length)];
        koi.Add(new Fish
        {
            Pos = new PointF(rng.Next(30, PondW - 30), rng.Next(30, PondH - 30)),
            Heading = (float)(rng.NextDouble() * Math.Tau),
            Speed = (0.5f + (float)rng.NextDouble() * 0.35f) * liveliness,
            Size = 7f + (float)rng.NextDouble() * 2.5f,
            Body = body,
            Patch = patch,
            WanderPhase = (float)(rng.NextDouble() * Math.Tau),
        });
    }

    void Tick()
    {
        clock += 0.033f;

        // Indexed, and new fish are queued rather than added inline: feeding
        // can hatch a koi mid-loop, which would invalidate a foreach.
        for (int i = 0; i < koi.Count; i++) Swim(koi[i]);
        while (pendingKoi > 0 && koi.Count < maxKoi) { AddKoi(); pendingKoi--; }
        pendingKoi = 0;

        for (int i = pellets.Count - 1; i >= 0; i--)
        {
            pellets[i].Sink += 0.12f;
            pellets[i].Life -= 0.0016f;
            if (pellets[i].Life <= 0) pellets.RemoveAt(i);
        }

        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            ripples[i].Radius += 0.9f;
            ripples[i].Life -= 0.03f;
            if (ripples[i].Life <= 0) ripples.RemoveAt(i);
        }

        Invalidate();
    }

    void Swim(Fish fish)
    {
        var target = Nearest(fish);
        float desired;

        if (target is not null)
        {
            desired = MathF.Atan2(target.Pos.Y - fish.Pos.Y, target.Pos.X - fish.Pos.X);
        }
        else
        {
            fish.WanderPhase += 0.02f;
            desired = fish.Heading + MathF.Sin(fish.WanderPhase) * 0.5f;

            // Steer away from the rim before touching it, so fish curve along
            // the edge instead of pinballing off it.
            const float margin = 26f;
            var centre = new PointF(PondW / 2f, PondH / 2f);
            if (fish.Pos.X < margin || fish.Pos.X > PondW - margin ||
                fish.Pos.Y < margin || fish.Pos.Y > PondH - margin)
                desired = MathF.Atan2(centre.Y - fish.Pos.Y, centre.X - fish.Pos.X);
        }

        float delta = NormalizeAngle(desired - fish.Heading);
        float turnRate = target is not null ? 0.14f : 0.06f;
        fish.Heading += Math.Clamp(delta, -turnRate, turnRate);

        float speed = fish.Speed * (target is not null ? 2.1f : 1f);
        fish.Pos = new PointF(
            Math.Clamp(fish.Pos.X + MathF.Cos(fish.Heading) * speed, 6, PondW - 6),
            Math.Clamp(fish.Pos.Y + MathF.Sin(fish.Heading) * speed, 6, PondH - 6));

        if (target is null) return;

        float dx = target.Pos.X - fish.Pos.X, dy = target.Pos.Y - fish.Pos.Y;
        if (dx * dx + dy * dy > fish.Size * fish.Size) return;

        pellets.Remove(target);
        fish.Fed++;
        totalFed++;
        fish.Size = Math.Min(12.5f, fish.Size + 0.22f);
        ripples.Add(new Ripple { Pos = fish.Pos, Radius = 2 });

        if (totalFed > best) { best = totalFed; ScoreStore.Save(Title, best); }
        if (totalFed % FeedsPerKoi == 0 && koi.Count + pendingKoi < maxKoi) pendingKoi++;

        Announce();
    }

    Pellet? Nearest(Fish fish)
    {
        Pellet? best = null;
        float bestDist = float.MaxValue;
        foreach (var p in pellets)
        {
            float dx = p.Pos.X - fish.Pos.X, dy = p.Pos.Y - fish.Pos.Y;
            float d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    static float NormalizeAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    void DropFood(int x, int y)
    {
        pellets.Add(new Pellet { Pos = new PointF(x, y) });
        ripples.Add(new Ripple { Pos = new PointF(x, y), Radius = 1 });
        timer.Start();
        Announce();
    }

    protected override bool OnGameKey(Keys key)
    {
        // Arrows are claimed so they cannot walk the ToolStrip's selection off
        // the pond; each one scatters food on that side.
        switch (key)
        {
            case Keys.Left: DropFood(PondW / 4, PondH / 2); return true;
            case Keys.Right: DropFood(PondW * 3 / 4, PondH / 2); return true;
            case Keys.Up: DropFood(PondW / 2, PondH / 4); return true;
            case Keys.Down: DropFood(PondW / 2, PondH * 3 / 4); return true;
            case Keys.Space:
            case Keys.Enter:
                for (int i = 0; i < 5; i++)
                    DropFood(rng.Next(20, PondW - 20), rng.Next(20, PondH - 20));
                return true;
            default:
                return false;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        DropFood(Math.Clamp(e.X, 8, PondW - 8), Math.Clamp(e.Y, 8, PondH - 8));
    }

    // ---- painting ---------------------------------------------------------

    protected override void PaintGame(Graphics g)
    {
        var pond = new RectangleF(0, 0, PondW, PondH);
        using (var path = Draw.RoundedPath(pond, 8))
        using (var water = new LinearGradientBrush(pond,
                   Color.FromArgb(18, 48, 52), Color.FromArgb(12, 26, 32), 60f))
        {
            g.FillPath(water, path);
            g.SetClip(path);
        }

        for (int i = 0; i < 5; i++)
        {
            float t = clock * 0.25f + i * 1.7f;
            float cx = PondW / 2f + MathF.Sin(t) * (40 + i * 12);
            float cy = PondH / 2f + MathF.Cos(t * 0.8f) * (34 + i * 10);
            using var glow = new SolidBrush(Color.FromArgb(9, 180, 235, 225));
            g.FillEllipse(glow, cx - 34, cy - 22, 68, 44);
        }

        foreach (var r in ripples)
        {
            using var pen = new Pen(Color.FromArgb((int)(110 * r.Life), 200, 240, 240), 1.2f);
            g.DrawEllipse(pen, r.Pos.X - r.Radius, r.Pos.Y - r.Radius, r.Radius * 2, r.Radius * 2);
        }

        foreach (var p in pellets)
        {
            float a = Math.Clamp(p.Life, 0f, 1f);
            using var brush = new SolidBrush(Color.FromArgb((int)(230 * a), 226, 178, 96));
            g.FillEllipse(brush, p.Pos.X - 2f, p.Pos.Y - 2f, 4f, 4f);
        }

        foreach (var fish in koi) DrawFish(g, fish);

        g.ResetClip();

        if (koi.Count > 0 && totalFed == 0 && pellets.Count == 0)
            Draw.CenteredText(g, "click the water to feed them", SmallFont, Color.FromArgb(150, Theme.Text),
                new RectangleF(0, PondH - 26, PondW, 16));
    }

    void DrawFish(Graphics g, Fish fish)
    {
        var state = g.Save();
        g.TranslateTransform(fish.Pos.X, fish.Pos.Y);
        g.RotateTransform(fish.Heading * 180f / MathF.PI);

        float len = fish.Size * 2.1f, wide = fish.Size * 0.92f;

        // The tail sweeps with a phase offset per fish so a school never looks
        // like one sprite drawn several times.
        float wag = MathF.Sin(clock * 6f + fish.WanderPhase) * (wide * 0.5f);
        using (var tail = new SolidBrush(Color.FromArgb(190, fish.Body)))
            g.FillPolygon(tail, new[]
            {
                new PointF(-len * 0.42f, 0),
                new PointF(-len * 0.82f, wag - wide * 0.42f),
                new PointF(-len * 0.82f, wag + wide * 0.42f),
            });

        using (var body = new SolidBrush(fish.Body))
            g.FillEllipse(body, -len / 2f, -wide / 2f, len, wide);

        using (var patch = new SolidBrush(Color.FromArgb(215, fish.Patch)))
        {
            g.FillEllipse(patch, -len * 0.06f, -wide * 0.44f, len * 0.34f, wide * 0.6f);
            g.FillEllipse(patch, -len * 0.38f, -wide * 0.1f, len * 0.22f, wide * 0.42f);
        }

        using (var eye = new SolidBrush(Color.FromArgb(160, 20, 24, 28)))
            g.FillEllipse(eye, len * 0.28f, -wide * 0.24f, 1.8f, 1.8f);

        g.Restore(state);
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        using (var water = new SolidBrush(Color.FromArgb(18, 52, 58)))
            g.FillEllipse(water, 1 * u, 1 * u, 30 * u, 30 * u);

        using (var body = new SolidBrush(Color.FromArgb(244, 246, 248)))
            g.FillEllipse(body, 9 * u, 11 * u, 17 * u, 9 * u);
        using (var tail = new SolidBrush(Color.FromArgb(240, 156, 74)))
            g.FillPolygon(tail, new[]
            {
                new PointF(11 * u, 15.5f * u),
                new PointF(4 * u, 10 * u),
                new PointF(4 * u, 21 * u),
            });
        using (var patch = new SolidBrush(Color.FromArgb(232, 120, 62)))
            g.FillEllipse(patch, 15 * u, 11 * u, 7 * u, 5 * u);
        using var pellet = new SolidBrush(Color.FromArgb(226, 178, 96));
        g.FillEllipse(pellet, 22 * u, 4 * u, 5 * u, 5 * u);
    }

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        ctx.Log($"start         : koi={koi.Count} fed={totalFed}");

        // Drop food right in front of a fish and watch it close the distance:
        // that distance shrinking is the whole steering behaviour under test.
        var subject = koi[0];
        float ahead = 40f;
        int fx = (int)Math.Clamp(subject.Pos.X + MathF.Cos(subject.Heading) * ahead, 20, PondW - 20);
        int fy = (int)Math.Clamp(subject.Pos.Y + MathF.Sin(subject.Heading) * ahead, 20, PondH - 20);

        float Distance() => MathF.Sqrt(
            (subject.Pos.X - fx) * (subject.Pos.X - fx) + (subject.Pos.Y - fy) * (subject.Pos.Y - fy));

        float before = Distance();
        await ctx.Click(fx, fy, settle: 120);
        ctx.Log($"dropped food  : pellets={pellets.Count} distance={before:0.0}");
        await ctx.Delay(700);
        ctx.Log($"after 0.7s    : distance={Distance():0.0} (was {before:0.0})");
        ctx.Snap("1-swimming");

        await ctx.Delay(1500);
        ctx.Log($"after eating  : fed={totalFed} pelletsLeft={pellets.Count}");

        // Feeding enough should hatch new koi.
        for (int i = 0; i < 14; i++)
        {
            await ctx.Click(rng.Next(30, PondW - 30), rng.Next(30, PondH - 30), settle: 90);
        }
        await ctx.Delay(2500);
        ctx.Log($"after feeding : koi={koi.Count} fed={totalFed} best={best}");
        ctx.Snap("2-school");
    }
}
