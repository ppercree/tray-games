namespace TrayGames.Core;

/// One file per game under %APPDATA%\TrayGames, so two games can never trample
/// each other's best score.
public static class ScoreStore
{
    static string PathFor(string game)
    {
        string safe = string.Concat(game.Where(c => char.IsLetterOrDigit(c)));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrayGames", safe + ".txt");
    }

    public static int Load(string game)
    {
        try
        {
            string path = PathFor(game);
            return File.Exists(path) && int.TryParse(File.ReadAllText(path), out int v) ? v : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static void Save(string game, int value)
    {
        try
        {
            string path = PathFor(game);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value.ToString());
        }
        catch
        {
            // A read-only AppData costs the best score and nothing else.
        }
    }
}
