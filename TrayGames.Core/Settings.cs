namespace TrayGames.Core;

/// One tweakable option: a label, a fixed list of choices, and the chosen index.
/// Choices are a closed list rather than a number so the menu can show them all
/// and the stored value can never be out of range.
public sealed class Setting
{
    public Setting(string key, string label, string[] options, int defaultIndex)
    {
        Key = key;
        Label = label;
        Options = options;
        Index = Math.Clamp(defaultIndex, 0, options.Length - 1);
    }

    public string Key { get; }
    public string Label { get; }
    public string[] Options { get; }
    public int Index { get; set; }

    public string Value => Options[Index];
    public string MenuText => $"{Label}: {Value}";
}

/// One file per game beside its score, written on every change and again on
/// exit. Format is key=index, one per line.
public static class SettingsStore
{
    static string PathFor(string game)
    {
        string safe = string.Concat(game.Where(char.IsLetterOrDigit));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrayGames", safe + ".settings");
    }

    static Dictionary<string, int> ReadAll(string game)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            string path = PathFor(game);
            if (!File.Exists(path)) return values;

            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (int.TryParse(line[(eq + 1)..], out int v))
                    values[line[..eq]] = v;
            }
        }
        catch
        {
            // An unreadable file just means defaults.
        }
        return values;
    }

    public static int Load(string game, string key, int fallback)
        => ReadAll(game).TryGetValue(key, out int v) ? v : fallback;

    /// Used to notice edits made by another process - the launcher writing this
    /// same file while the game is running.
    public static DateTime LastWrite(string game)
    {
        try
        {
            string path = PathFor(game);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch
        {
            return default;
        }
    }

    public static void SaveAll(string game, IEnumerable<Setting> settings)
    {
        try
        {
            string path = PathFor(game);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, settings.Select(s => $"{s.Key}={s.Index}"));
        }
        catch
        {
            // A read-only AppData costs the settings and nothing else.
        }
    }
}

/// A game's option list, written out on demand so the launcher can render a
/// settings UI without referencing the game assembly. One line per setting:
/// setting|key|label|index|opt;opt;opt
public static class SettingsDump
{
    public static void Write(string title, IEnumerable<Setting> settings, string path)
    {
        var lines = new List<string> { "title|" + title };
        foreach (var s in settings)
            lines.Add($"setting|{s.Key}|{s.Label}|{s.Index}|{string.Join(";", s.Options)}");

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, lines);
    }

    public static (string title, List<Setting> settings) Read(string path)
    {
        string title = "";
        var settings = new List<Setting>();

        foreach (string line in File.ReadAllLines(path))
        {
            var parts = line.Split('|');
            if (parts.Length == 2 && parts[0] == "title") { title = parts[1]; continue; }
            if (parts.Length != 5 || parts[0] != "setting") continue;

            var options = parts[4].Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (options.Length == 0) continue;
            int.TryParse(parts[3], out int index);
            settings.Add(new Setting(parts[1], parts[2], options, index));
        }

        return (title, settings);
    }
}
