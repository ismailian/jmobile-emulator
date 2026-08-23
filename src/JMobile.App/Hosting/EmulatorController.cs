using System.Diagnostics;
using JMobile.Emulator.Library;
using JMobile.Emulator.Session;
using JMobile.Emulator.Suites;
using JMobile.Runtime.Host;

namespace JMobile.App.Hosting;

/// <summary>
/// Owns the library, the settings and whichever game is currently running.
/// </summary>
/// <remarks>
/// Deliberately free of any WinForms reference. Everything the shell does — importing a JAR,
/// launching a game, changing a setting — is a method here, which keeps the form to layout and
/// event wiring and means this logic can be driven from a test or a different frontend.
/// </remarks>
public sealed class EmulatorController : IDisposable
{
    private readonly GdiFontRasterizer _fonts = new();
    private readonly Stopwatch _playClock = new();
    private string? _playingId;

    public EmulatorController(string? dataRoot = null)
    {
        Paths = new EmulatorPaths(dataRoot);
        Settings = EmulatorSettings.Load(Paths.SettingsFile);
        Library = new GameLibrary(Paths.CatalogFile);

        Audio = new WinMmAudioHost
        {
            Enabled = Settings.AudioEnabled,
            MasterVolume = Settings.AudioVolume,
        };

        Network = new HttpNetworkHost { IsEnabled = Settings.NetworkEnabled };
    }

    public EmulatorPaths Paths { get; }

    public EmulatorSettings Settings { get; }

    public GameLibrary Library { get; }

    public WinMmAudioHost Audio { get; }

    public HttpNetworkHost Network { get; }

    public DesktopFeedbackHost Feedback { get; } = new();

    /// <summary>The running game, or null when nothing is loaded.</summary>
    public EmulatorSession? Session { get; private set; }

    public bool IsRunning => Session is { State: SessionState.Running or SessionState.Paused };

    /// <summary>Raised when a session starts or stops, so the shell can re-bind its controls.</summary>
    public event Action<EmulatorSession?>? SessionChanged;

    /// <summary>Raised for guest output and emulator diagnostics.</summary>
    public event Action<string, bool>? Logged;

    // ---------------------------------------------------------------------------------
    // Library
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Imports a JAR into the library, copying it into the emulator's own folder by default.
    /// </summary>
    /// <remarks>
    /// Copying means a game added from a download folder keeps working after that folder is
    /// cleared out, which is the single most common way a library entry goes stale.
    /// </remarks>
    public LibraryEntry AddGame(string jarPath)
    {
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("That file no longer exists.", jarPath);

        // Opening it first means a file that is not a suite is rejected before anything is
        // copied or written to the catalogue.
        MidletSuite suite = MidletSuite.Open(jarPath);

        string finalPath = jarPath;
        if (Settings.CopyImportedGames)
        {
            string destination = Path.Combine(Paths.GamesRoot, Path.GetFileName(jarPath));
            if (!PathsEqual(jarPath, destination))
            {
                destination = MakeUniquePath(destination);
                File.Copy(jarPath, destination, overwrite: false);
                CopyDescriptorBeside(jarPath, destination);
                finalPath = destination;
            }
        }

        LibraryEntry entry = Library.Add(finalPath);
        Log($"Added {suite.Name} ({suite.Midlets.Count} MIDlet(s)) from {Path.GetFileName(jarPath)}");
        return entry;
    }

    /// <summary>A JAD beside the JAR carries attributes the manifest may not, so it travels too.</summary>
    private static void CopyDescriptorBeside(string sourceJar, string destinationJar)
    {
        string sourceJad = Path.ChangeExtension(sourceJar, ".jad");
        if (!File.Exists(sourceJad)) return;

        File.Copy(sourceJad, Path.ChangeExtension(destinationJar, ".jad"), overwrite: true);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        string directory = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int suffix = 2; ; suffix++)
        {
            string candidate = Path.Combine(directory, $"{name} ({suffix}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Removes a game from the library, optionally deleting its saves as well.</summary>
    public void RemoveGame(LibraryEntry entry, bool deleteSaves)
    {
        if (Session?.Suite.JarPath == entry.JarPath) Stop();

        Library.Remove(entry.Id);

        if (!deleteSaves) return;

        string saves = Path.Combine(Paths.SavesRoot, entry.Id);
        if (Directory.Exists(saves)) Directory.Delete(saves, recursive: true);
    }

    // ---------------------------------------------------------------------------------
    // Sessions
    // ---------------------------------------------------------------------------------

    /// <summary>Stops anything running and starts the given game.</summary>
    public EmulatorSession Launch(LibraryEntry entry)
    {
        Stop();

        MidletSuite suite = MidletSuite.Open(entry.JarPath);
        DeviceProfile device = ResolveDevice(entry, suite);

        var session = new EmulatorSession(new EmulatorSessionOptions
        {
            Suite = suite,
            MidletClass = entry.MidletClass,
            Device = device,
            SaveDirectory = Paths.SaveDirectoryFor(entry.Id),
            FontRasterizer = _fonts,
            Audio = Audio,
            Network = Network,
            Feedback = Feedback,
        });

        session.Logged += (text, isError) => Logged?.Invoke(text, isError);
        session.StateChanged += state => Log($"Session state: {state}");

        Session = session;
        _playingId = entry.Id;
        _playClock.Restart();

        Library.NoteLaunched(entry.Id);
        SessionChanged?.Invoke(session);

        Log($"Launching {suite.Name} on {device.Name} ({device.ScreenWidth}x{device.ScreenHeight})");
        session.Start();

        return session;
    }

    /// <summary>
    /// Chooses a device profile: the game's own override, then the suite's declared size, then
    /// the global default.
    /// </summary>
    private DeviceProfile ResolveDevice(LibraryEntry entry, MidletSuite suite)
    {
        if (entry.DeviceProfile is { } name &&
            DeviceProfile.BuiltIn.FirstOrDefault(p => p.Name == name) is { } chosen)
        {
            return chosen;
        }

        // A suite that declares the screen it was drawn for is telling us something the user
        // cannot reasonably be expected to know, so it outranks the global default.
        if (suite.RequestedScreenSize is { } size &&
            DeviceProfile.ClosestTo(size.Width, size.Height) is { } matching)
        {
            Log($"{suite.Name} asks for {size.Width}x{size.Height}; using {matching.Name}");
            return matching;
        }

        return Settings.ResolveDevice();
    }

    public void Stop()
    {
        if (Session is not { } session) return;

        session.Stop();
        session.Dispose();
        Session = null;

        if (_playingId is { } id && _playClock.IsRunning)
        {
            _playClock.Stop();
            Library.NotePlayed(id, _playClock.Elapsed);
        }

        _playingId = null;
        SessionChanged?.Invoke(null);
    }

    // ---------------------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------------------

    /// <summary>Persists settings and applies the ones that take effect immediately.</summary>
    public void SaveSettings()
    {
        Audio.Enabled = Settings.AudioEnabled;
        Audio.MasterVolume = Settings.AudioVolume;
        Network.IsEnabled = Settings.NetworkEnabled;

        Settings.Save(Paths.SettingsFile);
    }

    private void Log(string message) => Logged?.Invoke(message + Environment.NewLine, false);

    public void Dispose()
    {
        Stop();
        SaveSettings();
        Audio.Dispose();
        Network.Dispose();
    }
}
