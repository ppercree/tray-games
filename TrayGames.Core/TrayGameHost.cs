using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TrayGames.Core;

/// Owns the tray icon and the dropdown the game lives in. One process per game,
/// so a crash, a stuck timer or a wedged AI can only ever take down its own game.
public sealed partial class TrayGameHost : ApplicationContext
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    readonly GameControl game;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu;
    readonly ToolStripLabel statusLabel;
    readonly Form? devWindow;
    readonly List<(ToolStripMenuItem item, Setting setting)> settingItems = new();
    bool keepOpenForSettings;

    internal ContextMenuStrip Menu => menu;
    internal GameControl Game => game;

    public TrayGameHost(GameControl game, bool dev = false)
    {
        this.game = game;
        game.Changed += (_, _) => RefreshLabels();
        game.SettingsReloaded += (_, _) => RefreshSettingItems();

        var titleLabel = new ToolStripLabel(game.Title)
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.Text,
            Margin = new Padding(8, 6, 8, 0),
        };

        statusLabel = new ToolStripLabel
        {
            Font = new Font("Segoe UI", 8f),
            ForeColor = Theme.TextDim,
            Margin = new Padding(8, 0, 8, 2),
        };

        var host = new ToolStripControlHost(game)
        {
            AutoSize = false,
            Size = game.Size,
            Margin = new Padding(8, 2, 8, 6),
            BackColor = Theme.Menu,
        };
        game.SizeChanged += (_, _) => host.Size = game.Size;

        menu = new ContextMenuStrip
        {
            Renderer = new DarkRenderer(),
            ShowImageMargin = false,
            BackColor = Theme.Menu,
            ForeColor = Theme.Text,
        };
        menu.Items.Add(titleLabel);
        menu.Items.Add(statusLabel);
        menu.Items.Add(host);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("New game", (_, _) => { game.NewGame(); game.Focus(); }));

        if (game.Settings.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var setting in game.Settings) menu.Items.Add(BuildSettingItem(setting));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Quit", (_, _) => Quit()));

        // Picking an option would otherwise dismiss the whole chain, which makes
        // changing two settings in a row needlessly tedious.
        menu.Closing += (_, e) =>
        {
            if (!keepOpenForSettings) return;
            keepOpenForSettings = false;
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };

        // Without the foreground grab the dropdown is visible but not active,
        // and every keystroke goes to whatever window was focused before.
        menu.Opened += (_, _) =>
        {
            SetForegroundWindow(menu.Handle);
            game.Focus();
        };
        menu.Closed += (_, _) => game.SuspendForMenu();

        tray = new NotifyIcon
        {
            Icon = BuildIcon(game),
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) menu.Show(Cursor.Position);
        };

        if (dev)
        {
            // A tray-only process owns no window, which makes it invisible to
            // screen-automation tooling; --dev gives it one so the menu can be
            // driven during development.
            devWindow = new Form
            {
                Text = game.Title,
                ClientSize = new Size(420, 110),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Theme.Menu,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedSingle,
            };
            devWindow.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Theme.TextDim,
                Text = $"dev window - right-click the {game.Title} tray icon to play."
                     + Environment.NewLine + "Closing this window quits the game.",
            });
            devWindow.FormClosed += (_, _) => Quit();
            devWindow.Show();
        }

        RefreshLabels();
    }

    ToolStripMenuItem BuildSettingItem(Setting setting)
    {
        var parent = new ToolStripMenuItem(setting.MenuText);

        if (parent.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            dropDown.BackColor = Theme.Menu;
            dropDown.ForeColor = Theme.Text;
            dropDown.ShowImageMargin = false;
            // The check margin stays: it is what marks the current choice.
            dropDown.ShowCheckMargin = true;
        }

        for (int i = 0; i < setting.Options.Length; i++)
        {
            int choice = i;
            var option = new ToolStripMenuItem(setting.Options[i]) { Checked = i == setting.Index };
            option.Click += (_, _) =>
            {
                keepOpenForSettings = true;
                game.ChangeSetting(setting, choice);
                RefreshSettingItems();
                game.Focus();
            };
            parent.DropDownItems.Add(option);
        }

        settingItems.Add((parent, setting));
        return parent;
    }

    void RefreshSettingItems()
    {
        foreach (var (item, setting) in settingItems)
        {
            item.Text = setting.MenuText;
            for (int i = 0; i < item.DropDownItems.Count; i++)
                if (item.DropDownItems[i] is ToolStripMenuItem option)
                    option.Checked = i == setting.Index;
        }
        RefreshLabels();
    }

    static ToolStripMenuItem Item(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    void RefreshLabels()
    {
        statusLabel.Text = game.Status;
        string tip = $"{game.Title} - {game.Status}";
        tray.Text = tip.Length > 62 ? tip[..62] : tip;
    }

    static Icon BuildIcon(GameControl game)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            game.PaintIcon(g, 32);
        }

        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Quit()
    {
        game.SaveSettings();
        tray.Visible = false;
        tray.Dispose();
        if (devWindow is { IsDisposed: false }) devWindow.Hide();
        ExitThread();
    }
}
