using System.Diagnostics;
using TrayGames.Core;

namespace TrayGames.Launcher;

/// The launcher deliberately does not reference any game assembly. It knows
/// only a filename and a process name, which is what keeps every game a
/// genuinely separate service rather than a plug-in loaded into this process.
internal sealed record GameEntry(string Name, string Exe, string Blurb, Color Accent, string Glyph)
{
    public static readonly GameEntry[] All =
    {
        new("Snake", "TrayGame.Snake", "steer, eat, do not fold", Theme.Green, "snake"),
        new("Minesweeper", "TrayGame.Minesweeper", "9x9 grid, ten mines", Theme.Red, "mine"),
        new("Tetris", "TrayGame.Tetris", "ghost piece, hard drop", Theme.Purple, "tetris"),
        new("Tic Tac Toe", "TrayGame.TicTacToe", "unbeatable minimax", Theme.Cyan, "ttt"),
        new("Connect Four", "TrayGame.ConnectFour", "six-ply alpha-beta", Theme.Yellow, "four"),
        new("Feed the Koi", "TrayGame.Koi", "a pond, not a game", Theme.Orange, "koi"),
        new("Stacks", "TrayGame.Stacks", "time the drop, keep the width", Theme.Blue, "stacks"),
    };

    public string Path => System.IO.Path.Combine(AppContext.BaseDirectory, Exe + ".exe");

    public bool Installed => File.Exists(Path);

    public Process[] Running()
    {
        try { return Process.GetProcessesByName(Exe); }
        catch { return Array.Empty<Process>(); }
    }

    public bool IsRunning => Running().Length > 0;

    public void Summon()
    {
        if (IsRunning || !Installed) return;
        Process.Start(new ProcessStartInfo(Path) { UseShellExecute = false });
    }

    /// Asks the game itself for its option list. The launcher deliberately does
    /// not know what a game's settings are - running the exe for a tenth of a
    /// second keeps the game the single source of truth.
    public (string title, List<Setting> settings)? LoadSettings()
    {
        if (!Installed) return null;

        string temp = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"traygames-{Exe}-{Guid.NewGuid():N}.txt");
        try
        {
            var info = new ProcessStartInfo(Path, $"--dump-settings \"{temp}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(info);
            if (process is null) return null;
            if (!process.WaitForExit(6000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (!File.Exists(temp)) return null;
            var dump = SettingsDump.Read(temp);
            return dump.settings.Count > 0 ? dump : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    /// Writes the same file the game writes. A running game notices within a
    /// second or so and applies the change without being restarted.
    public static void SaveSettings(string title, IEnumerable<Setting> settings)
        => SettingsStore.SaveAll(title, settings);

    public void Dismiss()
    {
        // Killing is safe here: every game writes its best score the moment it
        // changes, so there is never unsaved state to lose on exit.
        foreach (var p in Running())
        {
            // Waiting keeps IsRunning honest for the caller that repaints
            // straight after, instead of showing a game that is already gone.
            try { p.Kill(); p.WaitForExit(1500); }
            catch { /* already gone */ }
        }
    }
}
