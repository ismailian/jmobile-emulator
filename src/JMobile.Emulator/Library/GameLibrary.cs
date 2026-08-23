using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JMobile.Emulator.Suites;

namespace JMobile.Emulator.Library;

/// <summary>One game installed in the library.</summary>
public sealed class LibraryEntry
{
    /// <summary>Stable identity derived from the JAR's contents, not its path.</summary>
    public required string Id { get; init; }

    public required string Title { get; set; }

    public required string JarPath { get; set; }

    public string Vendor { get; set; } = "Unknown";

    public string Version { get; set; } = "1.0";

    /// <summary>MIDlet class to launch, or null to use the suite's first.</summary>
    public string? MidletClass { get; set; }

    /// <summary>Device profile name chosen for this game, or null for the global default.</summary>
    public string? DeviceProfile { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPlayedAt { get; set; }

    public int PlayCount { get; set; }

    /// <summary>Total time played, in seconds.</summary>
    public long PlaySeconds { get; set; }

    [JsonIgnore]
    public bool JarExists => File.Exists(JarPath);
}

/// <summary>
/// The catalogue of installed games, persisted as JSON beside the save data.
/// </summary>
/// <remarks>
/// A game's identity is a hash of its JAR, not its file path. That means moving a JAR does not
/// orphan its saves and playtime, and re-adding the same game twice from different folders
/// updates the existing entry instead of creating a duplicate.
/// </remarks>
public sealed class GameLibrary
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _catalogPath;
    private readonly List<LibraryEntry> _entries = [];
    private readonly Lock _gate = new();

    public GameLibrary(string catalogPath)
    {
        _catalogPath = catalogPath;
        Load();
    }

    /// <summary>Every installed game, most recently played first.</summary>
    public IReadOnlyList<LibraryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries
                    .OrderByDescending(entry => entry.LastPlayedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
        }
    }

    public event Action? Changed;

    /// <summary>
    /// Adds a JAR to the library, or refreshes the entry if the same game is already present.
    /// </summary>
    public LibraryEntry Add(string jarPath)
    {
        string id = ComputeId(jarPath);
        MidletSuite suite = MidletSuite.Open(jarPath);

        lock (_gate)
        {
            LibraryEntry? existing = _entries.FirstOrDefault(entry => entry.Id == id);
            if (existing is not null)
            {
                // The same game from a new location: point at the new file and keep the history.
                existing.JarPath = jarPath;
                existing.Title = suite.Name;
                existing.Vendor = suite.Vendor;
                existing.Version = suite.Version;
                Save();
                Changed?.Invoke();
                return existing;
            }

            var entry = new LibraryEntry
            {
                Id = id,
                Title = suite.Name,
                JarPath = jarPath,
                Vendor = suite.Vendor,
                Version = suite.Version,
                MidletClass = suite.Midlets.Count > 0 ? suite.Midlets[0].ClassName : null,
            };

            _entries.Add(entry);
            Save();
            Changed?.Invoke();
            return entry;
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _entries.RemoveAll(entry => entry.Id == id);
            Save();
        }

        Changed?.Invoke();
    }

    public LibraryEntry? Find(string id)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(entry => entry.Id == id);
        }
    }

    /// <summary>Records that a game was launched.</summary>
    public void NoteLaunched(string id)
    {
        lock (_gate)
        {
            if (_entries.FirstOrDefault(entry => entry.Id == id) is not { } entry) return;

            entry.LastPlayedAt = DateTimeOffset.UtcNow;
            entry.PlayCount++;
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Records how long a session lasted.</summary>
    public void NotePlayed(string id, TimeSpan duration)
    {
        lock (_gate)
        {
            if (_entries.FirstOrDefault(entry => entry.Id == id) is not { } entry) return;

            entry.PlaySeconds += (long)duration.TotalSeconds;
            Save();
        }

        Changed?.Invoke();
    }

    public void Update(LibraryEntry entry)
    {
        lock (_gate)
        {
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Identifies a suite by hashing its bytes, so identity survives renames and moves.
    /// </summary>
    private static string ComputeId(string jarPath)
    {
        using FileStream stream = File.OpenRead(jarPath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private void Load()
    {
        if (!File.Exists(_catalogPath)) return;

        try
        {
            string json = File.ReadAllText(_catalogPath);
            LibraryEntry[]? loaded = JsonSerializer.Deserialize<LibraryEntry[]>(json, SerializerOptions);
            if (loaded is not null) _entries.AddRange(loaded);
        }
        catch (Exception)
        {
            // A corrupt catalogue is recoverable: the games are still on disk and can be added
            // again, so starting with an empty library beats refusing to start at all.
            _entries.Clear();
        }
    }

    private void Save()
    {
        string? directory = Path.GetDirectoryName(_catalogPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temporary = _catalogPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, SerializerOptions));
        File.Move(temporary, _catalogPath, overwrite: true);
    }
}
