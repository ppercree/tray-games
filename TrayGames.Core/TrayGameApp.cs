namespace TrayGames.Core;

public static class TrayGameApp
{
    /// Every game's Main is one call to this. Flags: --dev shows a plain window
    /// beside the tray icon, --selftest <dir> runs the game's scripted proof.
    public static void Run(Func<GameControl> createGame, string[] args)
    {
        // ApplicationConfiguration.Initialize() is source-generated for
        // WindowsApplication projects only, and this lives in a library. The
        // game is built by a factory rather than passed in, because
        // SetCompatibleTextRenderingDefault throws once any control exists.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Nested dropdowns (the settings submenus) are built by the ToolStrip
        // manager, not by the strip that owns them, so a renderer set only on
        // the context menu leaves them stock-white.
        ToolStripManager.Renderer = new DarkRenderer();

        // Used by the launcher to learn a game's options without loading its
        // assembly: build the game, write the list, exit before any tray icon
        // appears.
        int dump = Array.IndexOf(args, "--dump-settings");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            var probe = createGame();
            SettingsDump.Write(probe.Title, probe.Settings, args[dump + 1]);
            return;
        }

        int i = Array.IndexOf(args, "--selftest");
        string? selfTestDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        var host = new TrayGameHost(createGame(), args.Contains("--dev") || selfTestDir is not null);
        if (selfTestDir is not null) host.StartSelfTest(selfTestDir);

        Application.Run(host);
    }
}
