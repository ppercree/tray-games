using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TrayGames.Core;

/// Drives a game through the real dropdown with real OS input. Calling the
/// control's handlers directly would prove nothing: the open question is always
/// whether the ToolStrip dropdown hands input to a hosted control at all, and
/// only events that travel the full injection -> message pump -> ToolStrip path
/// answer it.
public sealed partial class SelfTestContext
{
    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    const uint KeyUp = 0x0002, ExtendedKey = 0x0001;
    const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;

    readonly GameControl game;
    readonly ContextMenuStrip menu;
    readonly string dir;
    readonly List<string> log = new();

    internal SelfTestContext(GameControl game, ContextMenuStrip menu, string dir)
    {
        this.game = game;
        this.menu = menu;
        this.dir = dir;
    }

    public GameControl Game => game;

    public void Log(string line) => log.Add(line);

    public Task Delay(int ms) => Task.Delay(ms);

    /// The Keys enum values are virtual key codes, so the cast is the mapping.
    /// Arrows are extended keys and need the flag to arrive as arrows.
    public void Press(Keys key)
    {
        bool extended = key is Keys.Left or Keys.Right or Keys.Up or Keys.Down;
        uint flags = extended ? ExtendedKey : 0;
        keybd_event((byte)key, 0, flags, UIntPtr.Zero);
        keybd_event((byte)key, 0, flags | KeyUp, UIntPtr.Zero);
    }

    public async Task Tap(Keys key, int settle = 180)
    {
        Press(key);
        await Delay(settle);
    }

    /// Coordinates are client coordinates of the game control; they are mapped
    /// to the screen so the click lands inside the dropdown and cannot dismiss it.
    public async Task Click(int x, int y, bool right = false, int settle = 220)
    {
        Cursor.Position = game.PointToScreen(new Point(x, y));
        await Delay(40);
        mouse_event(right ? RightDown : LeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(right ? RightUp : LeftUp, 0, 0, 0, UIntPtr.Zero);
        await Delay(settle);
    }

    /// Clicks an absolute screen point, for menu chrome that lives outside the
    /// game control's own coordinate space.
    public async Task ClickScreen(Point screen, int settle = 260)
    {
        Cursor.Position = screen;
        await Delay(60);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        await Delay(settle);
    }

    /// Reads one screen pixel, for checking that a window actually rendered in
    /// the colour it was told to rather than trusting a screenshot's appearance.
    public string ProbePixel(Point screen)
    {
        using var bmp = new Bitmap(1, 1);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(screen, Point.Empty, new Size(1, 1));
        var c = bmp.GetPixel(0, 0);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2} at {screen.X},{screen.Y}";
    }

    /// Captures several screen rectangles onto one flat backdrop. A plain union
    /// capture would also pick up whatever desktop happens to sit in the gap
    /// between a menu and its submenu.
    public void SnapComposite(string name, params Rectangle[] parts)
    {
        var valid = parts.Where(r => r.Width > 0 && r.Height > 0).ToArray();
        if (valid.Length == 0) { log.Add($"[{name}] nothing to capture"); return; }

        var union = valid.Aggregate(Rectangle.Union);
        using var bmp = new Bitmap(union.Width + 24, union.Height + 24);
        using (var g = Graphics.FromImage(bmp))
        {
            using (var bg = new SolidBrush(Theme.BoardInner))
                g.FillRectangle(bg, 0, 0, bmp.Width, bmp.Height);

            foreach (var r in valid)
                g.CopyFromScreen(r.Location,
                    new Point(r.X - union.X + 12, r.Y - union.Y + 12), r.Size);
        }
        bmp.Save(Path.Combine(dir, $"{name}.png"), ImageFormat.Png);
    }

    public void SnapRegion(string name, Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0) { log.Add($"[{name}] empty region"); return; }
        using var bmp = new Bitmap(region.Width, region.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        bmp.Save(Path.Combine(dir, $"{name}.png"), ImageFormat.Png);
    }

    public void Snap(string name)
    {
        var r = menu.Bounds;
        if (r.Width <= 0 || r.Height <= 0)
        {
            log.Add($"[{name}] menu has no bounds");
            return;
        }

        using var bmp = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        bmp.Save(Path.Combine(dir, $"{name}.png"), ImageFormat.Png);
    }

    internal void Write()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "selftest.log"), log);
    }
}

public sealed partial class TrayGameHost
{
    internal async void StartSelfTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var ctx = new SelfTestContext(game, menu, dir);

        try
        {
            devWindow?.Activate();
            await Task.Delay(600);

            // A scripted run cannot stop another process taking the foreground,
            // which closes a normal dropdown mid-test. Auto-close behaviour
            // itself is checked separately, before this is switched off.
            menu.AutoClose = true;
            menu.Show(new Point(700, 340));
            await Task.Delay(450);

            ctx.Log($"game          : {game.Title}");
            ctx.Log($"menu.Visible  : {menu.Visible}  bounds={menu.Bounds}");
            ctx.Log($"focus         : Focused={game.Focused} ContainsFocus={game.ContainsFocus}");
            ctx.Log($"status        : {game.Status}");
            ctx.Snap("0-open");

            menu.AutoClose = false;
            ctx.Log($"focus after pin: Focused={game.Focused}");
            game.Focus();
            await Task.Delay(120);
            ctx.Log($"focus at start : Focused={game.Focused}");

            await game.SelfTestAsync(ctx);

            await CheckSettings(ctx);

            ctx.Log($"final status  : {game.Status}");
            ctx.Log($"menu open     : {menu.Visible}");
        }
        catch (Exception ex)
        {
            ctx.Log("EXCEPTION: " + ex);
        }

        menu.AutoClose = true;
        ctx.Write();
        Quit();
    }

    /// Exercises the settings submenu the way a person would: open it, click an
    /// option, and check that the choice applied, the menu survived, and the
    /// value reached disk.
    async Task CheckSettings(SelfTestContext ctx)
    {
        if (settingItems.Count == 0) { ctx.Log("settings      : none declared"); return; }

        ctx.Log($"settings      : {string.Join(", ", game.Settings.Select(s => s.MenuText))}");

        var (item, setting) = settingItems[0];
        int before = setting.Index;
        int target = (before + 1) % setting.Options.Length;

        item.ShowDropDown();
        await Task.Delay(700);
        ctx.Log($"submenu open  : {item.DropDown.Visible} for '{item.Text}'");

        var dd = item.DropDown.Bounds;
        ctx.Log($"submenu bounds: {dd}");
        ctx.Log($"submenu bg px : {ctx.ProbePixel(new Point(dd.Right - 10, dd.Top + 6))}");
        if (devWindow is { IsDisposed: false })
            ctx.Log($"dev window px : {ctx.ProbePixel(new Point(devWindow.Bounds.Right - 30, devWindow.Bounds.Bottom - 30))}");
        ctx.SnapComposite("8-settings", menu.Bounds, item.DropDown.Bounds);

        var option = item.DropDownItems[target];
        var point = item.DropDown.PointToScreen(new Point(
            option.Bounds.Left + option.Bounds.Width / 2,
            option.Bounds.Top + option.Bounds.Height / 2));
        await ctx.ClickScreen(point, 420);

        ctx.Log($"picked '{setting.Options[target]}' -> index={setting.Index} (was {before})");
        ctx.Log($"menu still open after pick: {menu.Visible}");
        ctx.Log($"persisted     : {setting.Key}={SettingsStore.Load(game.Title, setting.Key, -1)}");
        ctx.Log($"board size    : {game.Size.Width}x{game.Size.Height}");
        ctx.Snap("9-after-setting");

        // Simulate the launcher editing the same file from another process: the
        // running game should notice and apply it without being restarted.
        int outside = (setting.Index + 1) % setting.Options.Length;
        var edited = game.Settings.Select(s =>
            new Setting(s.Key, s.Label, s.Options, s == setting ? outside : s.Index)).ToList();
        SettingsStore.SaveAll(game.Title, edited);
        ctx.Log($"outside edit  : {setting.Key}={outside} written by another writer");

        await Task.Delay(2600);
        ctx.Log($"game reloaded : {setting.Key} is now {setting.Index} " +
                $"({(setting.Index == outside ? "picked up" : "MISSED")})");
    }
}
