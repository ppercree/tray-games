namespace TrayGames.Launcher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Same chrome as the game menus, including the icon margin of the tray
        // list, which the manager renders rather than the strip itself.
        ToolStripManager.Renderer = new TrayGames.Core.DarkRenderer();
        var form = new LauncherForm();

        // The launcher is an ordinary window, but screen-automation tooling
        // cannot resolve an unsigned local exe by name, so it photographs
        // itself on request.
        int i = Array.IndexOf(args, "--shot");
        if (i >= 0 && i + 1 < args.Length) form.ShotTo(args[i + 1]);

        int j = Array.IndexOf(args, "--selftest");
        if (j >= 0 && j + 1 < args.Length) form.RunSelfTest(args[j + 1]);

        Application.Run(form);
    }
}
