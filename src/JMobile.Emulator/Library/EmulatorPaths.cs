namespace JMobile.Emulator.Library;

/// <summary>
/// Where the emulator keeps its data on disk.
/// </summary>
/// <remarks>
/// Everything lives under one folder in the user's application data so that the whole install —
/// library, saves, settings — can be backed up or moved by copying a single directory.
/// </remarks>
public sealed class EmulatorPaths
{
    public EmulatorPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JMobile");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SavesRoot);
        Directory.CreateDirectory(GamesRoot);
    }

    public string Root { get; }

    /// <summary>Per-game record store directories live under here, one folder per game id.</summary>
    public string SavesRoot => Path.Combine(Root, "saves");

    /// <summary>Copies of imported JARs, so the library survives the original being moved.</summary>
    public string GamesRoot => Path.Combine(Root, "games");

    public string CatalogFile => Path.Combine(Root, "library.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogFile => Path.Combine(Root, "jmobile.log");

    /// <summary>The record store directory for one game, created on demand.</summary>
    public string SaveDirectoryFor(string gameId)
    {
        string path = Path.Combine(SavesRoot, gameId);
        Directory.CreateDirectory(path);
        return path;
    }
}
