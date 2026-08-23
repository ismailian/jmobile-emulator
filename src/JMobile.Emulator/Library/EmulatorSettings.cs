using System.Text.Json;
using System.Text.Json.Serialization;
using JMobile.Runtime.Host;

namespace JMobile.Emulator.Library;

/// <summary>How the phone screen is scaled into the window.</summary>
public enum ScalingMode
{
    /// <summary>One phone pixel per screen pixel.</summary>
    Original,

    /// <summary>Largest whole multiple that fits, which keeps pixel art crisp.</summary>
    IntegerFit,

    /// <summary>Fill the window, preserving aspect ratio.</summary>
    Stretch,
}

/// <summary>User preferences, persisted between runs.</summary>
public sealed class EmulatorSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Name of the default device profile, matched against the built-in list.</summary>
    public string DeviceProfileName { get; set; } = DeviceProfile.Series60.Name;

    public ScalingMode Scaling { get; set; } = ScalingMode.IntegerFit;

    /// <summary>Draw the handset body and keypad around the screen.</summary>
    public bool ShowPhoneSkin { get; set; } = true;

    public bool ShowDiagnostics { get; set; }

    public bool AudioEnabled { get; set; } = true;

    public int AudioVolume { get; set; } = 70;

    /// <summary>
    /// Whether games may reach the network.
    /// </summary>
    /// <remarks>
    /// Off by default. These games call servers that no longer exist, and the MIDP networking
    /// API is blocking, so leaving it on mostly buys stalls on requests that cannot succeed.
    /// </remarks>
    public bool NetworkEnabled { get; set; }

    /// <summary>Keyboard bindings, from handset key code to a Windows virtual key.</summary>
    public Dictionary<string, int> KeyBindings { get; set; } = [];

    /// <summary>Copy imported JARs into the library folder instead of referencing in place.</summary>
    public bool CopyImportedGames { get; set; } = true;

    public static EmulatorSettings Load(string path)
    {
        if (!File.Exists(path)) return new EmulatorSettings();

        try
        {
            return JsonSerializer.Deserialize<EmulatorSettings>(File.ReadAllText(path), SerializerOptions)
                   ?? new EmulatorSettings();
        }
        catch (Exception)
        {
            // Settings are conveniences, never data. Falling back to defaults is always better
            // than refusing to start over a malformed preferences file.
            return new EmulatorSettings();
        }
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, SerializerOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Resolves the configured profile name against the built-in profiles.</summary>
    public DeviceProfile ResolveDevice() =>
        DeviceProfile.BuiltIn.FirstOrDefault(
            profile => string.Equals(profile.Name, DeviceProfileName, StringComparison.Ordinal))
        ?? DeviceProfile.Series60;
}
