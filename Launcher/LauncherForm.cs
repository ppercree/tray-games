using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TrayGames.Core;

namespace TrayGames.Launcher;

internal sealed class LauncherForm : Form
{
    readonly List<GameCard> cards = new();
    readonly System.Windows.Forms.Timer poll = new() { Interval = 1000 };
    readonly NotifyIcon tray;
    readonly ContextMenuStrip trayMenu;
    readonly System.Windows.Forms.Timer trayListGuard = new() { Interval = 700 };
    bool keepTrayListOpen;
    readonly List<string> closeReasons = new();

    public LauncherForm()
    {
        Text = "Tray Games";
        ClientSize = new Size(476, 528);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Menu;
        ForeColor = Theme.Text;
        DoubleBuffered = true;
        Icon = BuildIcon();

        for (int i = 0; i < GameEntry.All.Length; i++)
        {
            var entry = GameEntry.All[i];
            var card = new GameCard(entry)
            {
                Location = new Point(16 + (i % 2) * 228, 62 + (i / 2) * 104),
            };
            card.StateChanged += (_, _) => UpdateCards();
            card.SettingsRequested += (_, _) => OpenSettings(entry);
            cards.Add(card);
            Controls.Add(card);
        }

        Controls.Add(FooterButton("Summon all", 16, 484, 104, () =>
        {
            foreach (var entry in GameEntry.All) entry.Summon();
        }));
        Controls.Add(FooterButton("Dismiss all", 128, 484, 104, () =>
        {
            foreach (var entry in GameEntry.All) entry.Dismiss();
        }));
        Controls.Add(FooterButton("Minimise to tray", 240, 484, 140, MinimiseToTray));

        trayMenu = BuildTrayMenu();
        tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "Tray Games",
            ContextMenuStrip = trayMenu,
            Visible = false,
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) RestoreFromTray();
        };

        poll.Tick += (_, _) => UpdateCards();
        poll.Start();
        UpdateCards();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(Handle);
    }

    // ---- tray mode --------------------------------------------------------

    /// Deliberately bare: an icon and a name per game and nothing else. The
    /// cards in the window are where the detail lives.
    ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayListRenderer(),
            BackColor = Theme.Menu,
            ForeColor = Theme.Text,
        };

        foreach (var entry in GameEntry.All)
        {
            var item = new ToolStripMenuItem(entry.Name, GlyphImage(entry, 16))
            {
                Font = new Font("Segoe UI", 9f),
                Tag = entry,
            };
            // The guard is armed on mouse-down, not in the click handler: for a
            // top-level item the dropdown's Closing fires before Click, so a
            // flag set during Click is always too late.
            item.MouseDown += (_, _) => ArmTrayListGuard();
            item.Click += (_, _) => ToggleFromTray(entry, menu);
            menu.Items.Add(item);
        }

        // Toggling should not dismiss the list: summoning three games in a row
        // is the normal case, and the outlines are the feedback for it. The
        // guard covers a window rather than a single event, because dismissing
        // a game blocks briefly on the process exiting and the foreground moves
        // while it does - which closes the list as AppFocusChange, not as a
        // click.
        trayListGuard.Tick += (_, _) => { trayListGuard.Stop(); keepTrayListOpen = false; };
        menu.Closing += (_, e) =>
        {
            // Kept for the self-test; capped so a long session cannot grow it.
            if (closeReasons.Count > 8) closeReasons.RemoveAt(0);
            closeReasons.Add($"{e.CloseReason}/guard={keepTrayListOpen}");
            if (!keepTrayListOpen) return;
            if (e.CloseReason is ToolStripDropDownCloseReason.ItemClicked
                              or ToolStripDropDownCloseReason.AppFocusChange)
                e.Cancel = true;
        };

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("Open Tray Games", RestoreFromTray));
        menu.Items.Add(MenuItem("Quit launcher", Close));
        return menu;
    }

    void ArmTrayListGuard()
    {
        keepTrayListOpen = true;
        trayListGuard.Stop();
        trayListGuard.Start();
    }

    void ToggleFromTray(GameEntry entry, ContextMenuStrip menu)
    {
        ArmTrayListGuard();

        if (entry.IsRunning) entry.Dismiss();
        else entry.Summon();

        UpdateCards();
        menu.Refresh();
    }

    static ToolStripMenuItem MenuItem(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    static Image GlyphImage(GameEntry entry, int size)
    {
        // Drawn large and scaled down: these glyphs are line art, and painting
        // them straight into 16 pixels loses the strokes.
        using var large = new Bitmap(size * 3, size * 3);
        using (var g = Graphics.FromImage(large))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            Glyphs.Paint(entry.Glyph, g, new RectangleF(0, 0, size * 3, size * 3), entry.Accent);
        }

        var small = new Bitmap(size, size);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            g.DrawImage(large, new Rectangle(0, 0, size, size));
        }
        return small;
    }

    public void MinimiseToTray()
    {
        tray.Visible = true;
        Hide();
    }

    public void RestoreFromTray()
    {
        tray.Visible = false;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    // ---- settings ---------------------------------------------------------

    void OpenSettings(GameEntry entry)
    {
        Cursor = Cursors.WaitCursor;
        var dump = entry.LoadSettings();
        Cursor = Cursors.Default;

        if (dump is null)
        {
            MessageBox.Show(this,
                $"{entry.Name} did not report any settings.",
                "Tray Games", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SettingsDialog(entry, dump.Value.title, dump.Value.settings);
        dialog.ShowDialog(this);
    }

    // ---- chrome -----------------------------------------------------------

    Button FooterButton(string text, int x, int y, int width, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Tile,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Theme.Separator;
        button.FlatAppearance.MouseOverBackColor = Theme.TileLit;
        button.Click += (_, _) => { onClick(); UpdateCards(); };
        return button;
    }

    void UpdateCards()
    {
        for (int i = 0; i < cards.Count; i++)
            cards[i].Refresh(GameEntry.All[i].IsRunning);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var title = new SolidBrush(Theme.Text))
            g.DrawString("Tray Games", new Font("Segoe UI", 14f, FontStyle.Bold), title, 16, 16);

        int running = GameEntry.All.Count(entry => entry.IsRunning);
        using (var sub = new SolidBrush(Theme.TextDim))
            g.DrawString(
                running == 0
                    ? "summon a game, then right-click its tray icon to play"
                    : $"{running} in your tray - right-click a tray icon to play",
                new Font("Segoe UI", 8.5f), sub, 18, 40);

        using var pen = new Pen(Theme.Separator);
        g.DrawLine(pen, 16, 474, ClientSize.Width - 16, 474);
    }

    // ---- self test --------------------------------------------------------

    internal async void ShotTo(string path)
    {
        // CopyFromScreen reads the desktop, so the window has to genuinely be in
        // front of everything else first.
        TopMost = true;
        Activate();
        BringToFront();
        await Task.Delay(1200);

        Shoot(path);
        Close();
    }

    /// Summons every game, checks each really became its own process, exercises
    /// the tray list and the settings round trip, then dismisses them again.
    internal async void RunSelfTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new List<string>();

        TopMost = true;
        Activate();
        await Task.Delay(700);

        foreach (var entry in GameEntry.All) entry.Summon();
        await Task.Delay(2600);
        UpdateCards();
        await Task.Delay(400);

        foreach (var entry in GameEntry.All)
        {
            var procs = entry.Running();
            log.Add($"{entry.Name,-14} running={procs.Length > 0,-5} pids=[{string.Join(",", procs.Select(p => p.Id))}]");
        }
        log.Add($"distinct processes: {GameEntry.All.Sum(entry => entry.Running().Length)}");

        // Starting seven processes moves the foreground around, so the window
        // has to be pulled back in front before it photographs itself.
        TopMost = false;
        TopMost = true;
        Activate();
        BringToFront();
        await Task.Delay(800);
        Shoot(Path.Combine(dir, "launcher-running.png"));

        await CheckSettingsRoundTrip(log, dir);
        await CheckTrayMode(log, dir);

        foreach (var entry in GameEntry.All) entry.Dismiss();
        await Task.Delay(1400);
        UpdateCards();
        log.Add($"after dismiss all, still running: {GameEntry.All.Count(e => e.IsRunning)}");

        File.WriteAllLines(Path.Combine(dir, "launcher.log"), log);
        Close();
    }

    async Task CheckSettingsRoundTrip(List<string> log, string dir)
    {
        var entry = GameEntry.All.First(e => e.Name == "Snake");

        var dump = entry.LoadSettings();
        if (dump is null) { log.Add("settings dump : FAILED"); return; }

        var (title, settings) = dump.Value;
        log.Add($"settings dump : {title} -> {string.Join(", ", settings.Select(s => s.MenuText))}");

        var dialog = new SettingsDialog(entry, title, settings);
        dialog.Show(this);
        dialog.Location = new Point(Bounds.Right + 20, Bounds.Top + 40);
        await Task.Delay(800);
        Shoot(Path.Combine(dir, "launcher-settings.png"), dialog.Bounds);

        var target = settings[0];
        int before = target.Index;
        target.Index = (before + 1) % target.Options.Length;
        GameEntry.SaveSettings(title, settings);
        log.Add($"wrote '{target.Label}' {before} -> {target.Index} while {entry.Name} was running");

        await Task.Delay(2500);
        log.Add($"settings file : {target.Key}={SettingsStore.Load(title, target.Key, -1)}");

        dialog.Close();
        dialog.Dispose();
        await Task.Delay(300);
    }

    async Task CheckTrayMode(List<string> log, string dir)
    {
        MinimiseToTray();
        await Task.Delay(800);
        log.Add($"minimised     : windowVisible={Visible} trayIconVisible={tray.Visible}");

        trayMenu.Show(new Point(700, 380));
        await Task.Delay(700);
        int withIcons = trayMenu.Items.OfType<ToolStripMenuItem>().Count(i => i.Image is not null);
        log.Add($"tray list     : {trayMenu.Items.Count} items, {withIcons} game entries with icons");

        Shoot(Path.Combine(dir, "tray-list-all.png"), trayMenu.Bounds);
        await CheckTrayToggle(log);

        // A capture with every game running shows seven identical outlines and
        // proves nothing; dismiss a few so the state is legible.
        foreach (var name in new[] { "Tetris", "Tic Tac Toe", "Feed the Koi" })
        {
            ArmTrayListGuard();
            GameEntry.All.First(e => e.Name == name).Dismiss();
        }
        UpdateCards();
        trayMenu.Refresh();
        await Task.Delay(500);
        log.Add($"mixed state   : listOpen={trayMenu.Visible} running={GameEntry.All.Count(e => e.IsRunning)}");
        Shoot(Path.Combine(dir, "tray-list.png"), trayMenu.Bounds);
        trayMenu.Close();

        await Task.Delay(300);
        RestoreFromTray();
        await Task.Delay(600);
        log.Add($"restored      : windowVisible={Visible} trayIconVisible={tray.Visible}");
    }

    /// Clicks a game in the tray list twice: it should dismiss, then summon,
    /// with the list surviving both and the outline following the state.
    async Task CheckTrayToggle(List<string> log)
    {
        var entry = GameEntry.All.First(e => e.Name == "Stacks");
        var item = trayMenu.Items.OfType<ToolStripMenuItem>().First(i => ReferenceEquals(i.Tag, entry));

        string OutlinePixel()
        {
            var origin = trayMenu.PointToScreen(item.Bounds.Location);
            return GreenestAlong(origin.X, origin.Y + item.Height / 2, 8);
        }

        log.Add($"toggle start  : {entry.Name} running={entry.IsRunning} edge={OutlinePixel()}");

        closeReasons.Clear();
        await ClickItem(item);
        log.Add($"after click 1 : running={entry.IsRunning} listOpen={trayMenu.Visible} edge={OutlinePixel()}");
        log.Add($"close reasons : {string.Join(", ", closeReasons)}");

        await ClickItem(item);
        log.Add($"after click 2 : running={entry.IsRunning} listOpen={trayMenu.Visible} edge={OutlinePixel()}");
    }

    async Task ClickItem(ToolStripItem item)
    {
        var origin = trayMenu.PointToScreen(item.Bounds.Location);
        Cursor.Position = new Point(origin.X + item.Width / 2, origin.Y + item.Height / 2);
        await Task.Delay(120);
        MouseInput.Click();
        await Task.Delay(1600);
        trayMenu.Refresh();
        await Task.Delay(250);
    }

    /// Scans a short horizontal run and reports the most green pixel in it. A
    /// single-pixel probe misses a 1.4px anti-aliased stroke far too easily.
    static string GreenestAlong(int x, int y, int width)
    {
        using var bmp = new Bitmap(width, 1);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(new Point(x, y), Point.Empty, new Size(width, 1));

        Color best = bmp.GetPixel(0, 0);
        for (int i = 1; i < width; i++)
        {
            var c = bmp.GetPixel(i, 0);
            if (c.G - Math.Max(c.R, c.B) > best.G - Math.Max(best.R, best.B)) best = c;
        }
        return $"#{best.R:X2}{best.G:X2}{best.B:X2}";
    }

    void Shoot(string path, Rectangle? region = null)
    {
        var r = region ?? Bounds;
        if (r.Width <= 0 || r.Height <= 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            Draw.FillRounded(g, new RectangleF(2, 2, 13, 13), 3, Theme.Green);
            Draw.FillRounded(g, new RectangleF(17, 2, 13, 13), 3, Theme.Red);
            Draw.FillRounded(g, new RectangleF(2, 17, 13, 13), 3, Theme.Blue);
            Draw.FillRounded(g, new RectangleF(17, 17, 13, 13), 3, Theme.Yellow);
        }
        IntPtr handle = bmp.GetHicon();
        using var temp = Icon.FromHandle(handle);
        return (Icon)temp.Clone();
    }
}
