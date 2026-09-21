using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TrayGames.Core;

/// A game that lives inside a tray menu. Subclasses paint themselves and answer
/// keys; everything about the menu, the tray icon and focus is the host's job.
public abstract class GameControl : Control
{
    protected static readonly Font BigFont = new("Segoe UI", 10f, FontStyle.Bold);
    protected static readonly Font SmallFont = new("Segoe UI", 7.5f);
    protected static readonly Font TinyFont = new("Segoe UI", 6.5f);

    protected GameControl(int width, int height)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        BackColor = Theme.Board;
        Size = new Size(width, height);
    }

    /// Shown in bold at the top of the menu, and used as the settings key, so it
    /// must stay stable once a game has shipped.
    public abstract string Title { get; }

    /// The dim line under the title: score, mines left, whose turn it is.
    public abstract string Status { get; }

    public abstract void NewGame();

    // ---- settings ---------------------------------------------------------

    readonly List<Setting> settings = new();
    readonly System.Windows.Forms.Timer settingsWatch = new() { Interval = 1500 };
    DateTime lastSettingsWrite;

    public IReadOnlyList<Setting> Settings => settings;

    /// Raised when the settings file was changed by something else - the
    /// launcher - so the host can relabel its menu.
    public event EventHandler? SettingsReloaded;

    /// Declared by the game in its constructor, before the first NewGame. The
    /// stored choice wins over the default, so a game always starts the way it
    /// was last left.
    protected Setting AddSetting(string key, string label, string[] options, int defaultIndex)
    {
        var setting = new Setting(key, label, options, SettingsStore.Load(Title, key, defaultIndex));
        settings.Add(setting);

        if (settings.Count == 1)
        {
            // Polling a file stamp beats a FileSystemWatcher here: the watcher
            // raises events on a pool thread, and this control's handle may not
            // exist yet to marshal them back.
            lastSettingsWrite = SettingsStore.LastWrite(Title);
            settingsWatch.Tick += (_, _) => ReloadIfChangedExternally();
            settingsWatch.Start();
        }

        return setting;
    }

    void ReloadIfChangedExternally()
    {
        var stamp = SettingsStore.LastWrite(Title);
        if (stamp == lastSettingsWrite) return;
        lastSettingsWrite = stamp;

        bool changed = false;
        foreach (var setting in settings)
        {
            int stored = SettingsStore.Load(Title, setting.Key, setting.Index);
            stored = Math.Clamp(stored, 0, setting.Options.Length - 1);
            if (stored == setting.Index) continue;
            setting.Index = stored;
            changed = true;
        }

        if (!changed) return;
        ApplySettings();
        SettingsReloaded?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeSetting(Setting setting, int index)
    {
        if (setting.Index == index) return;
        setting.Index = Math.Clamp(index, 0, setting.Options.Length - 1);
        SaveSettings();
        ApplySettings();
    }

    public void SaveSettings()
    {
        if (settings.Count == 0) return;
        SettingsStore.SaveAll(Title, settings);
        // Remember our own write so it is not mistaken for an outside edit.
        lastSettingsWrite = SettingsStore.LastWrite(Title);
    }

    /// Most options change the board itself, so the default is to start over.
    /// Games whose options can be applied mid-run override this.
    protected virtual void ApplySettings() => NewGame();

    /// Games whose board geometry is itself a setting resize from ApplySettings;
    /// the host follows along through SizeChanged.
    protected void SetBoardSize(int width, int height)
    {
        if (Size.Width != width || Size.Height != height) Size = new Size(width, height);
    }

    /// Called when the menu closes. Anything with a clock should stop here, so
    /// dismissing the menu never costs a run.
    public virtual void SuspendForMenu() { }

    public event EventHandler? Changed;

    protected void Announce()
    {
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- input ------------------------------------------------------------

    /// Return true to consume the key. The ToolStrip dropdown uses arrows and
    /// space for menu navigation, so anything not claimed here is lost.
    protected abstract bool OnGameKey(Keys key);

    protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        => OnGameKey(keyData) || base.ProcessCmdKey(ref m, keyData);

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Space => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (OnGameKey(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    // ---- painting ---------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var bg = new SolidBrush(Theme.Board))
            e.Graphics.FillRectangle(bg, ClientRectangle);
        PaintGame(e.Graphics);
    }

    protected abstract void PaintGame(Graphics g);

    /// Draws this game's tray icon into a size x size box. Icons are painted at
    /// runtime so no project has to ship and version a matching .ico.
    public abstract void PaintIcon(Graphics g, int size);

    /// Optional scripted run used by --selftest. A tray-only process is
    /// invisible to screen-automation tooling, so each game proves itself.
    public virtual Task SelfTestAsync(SelfTestContext ctx) => Task.CompletedTask;

    /// Overlay used by most games for "press space", "you win" and the like.
    protected void DrawOverlay(Graphics g, RectangleF board, string big, string line2, string line3 = "", int alpha = 185)
    {
        using (var scrim = new SolidBrush(Color.FromArgb(alpha, Theme.BoardInner)))
            g.FillRectangle(scrim, board);

        float mid = board.Y + board.Height / 2f;
        float top = line3.Length > 0 ? mid - 22 : mid - 14;
        Draw.CenteredText(g, big, BigFont, Theme.Text, new RectangleF(board.X, top, board.Width, 20));
        Draw.CenteredText(g, line2, SmallFont, Theme.TextDim, new RectangleF(board.X, top + 20, board.Width, 16));
        if (line3.Length > 0)
            Draw.CenteredText(g, line3, SmallFont, Theme.TextDim, new RectangleF(board.X, top + 34, board.Width, 16));
    }
}
