using System.Text;
using JMobile.Core.Loading;

namespace JMobile.Emulator.Suites;

/// <summary>One entry from a suite's <c>MIDlet-n</c> attributes.</summary>
/// <param name="Name">Display name shown in the launcher.</param>
/// <param name="IconPath">Suite-relative path to the icon, or null.</param>
/// <param name="ClassName">Internal name of the MIDlet class to instantiate.</param>
public readonly record struct MidletEntry(string Name, string? IconPath, string ClassName);

/// <summary>
/// A loaded MIDlet suite: its JAR, its attributes, and the MIDlets it declares.
/// </summary>
/// <remarks>
/// Attributes may come from two places. The JAR's own manifest always exists; a JAD file
/// alongside it may add or override attributes, and is where a suite's real name and version
/// often live. JAD values win, which is what a real device's installer does.
/// </remarks>
public sealed class MidletSuite
{
    private MidletSuite(string jarPath, JarClassSource source, Dictionary<string, string> attributes)
    {
        JarPath = jarPath;
        Source = source;
        Attributes = attributes;
        Midlets = ParseMidlets(attributes);
    }

    public string JarPath { get; }

    public JarClassSource Source { get; }

    /// <summary>Merged manifest and JAD attributes, compared case-insensitively as the spec says.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    public IReadOnlyList<MidletEntry> Midlets { get; }

    public string Name => Attributes.GetValueOrDefault("MIDlet-Name")
                          ?? Path.GetFileNameWithoutExtension(JarPath);

    public string Vendor => Attributes.GetValueOrDefault("MIDlet-Vendor") ?? "Unknown";

    public string Version => Attributes.GetValueOrDefault("MIDlet-Version") ?? "1.0";

    public string? Description => Attributes.GetValueOrDefault("MIDlet-Description");

    /// <summary>The suite icon's bytes, if it declares one that exists in the JAR.</summary>
    public byte[]? IconData
    {
        get
        {
            string? path = Attributes.GetValueOrDefault("MIDlet-Icon") ?? Midlets.FirstOrDefault().IconPath;
            if (string.IsNullOrEmpty(path)) return null;

            return Source.TryGetResource(path, out byte[] data) ? data : null;
        }
    }

    /// <summary>Screen size the suite asks for, when it declares one.</summary>
    public (int Width, int Height)? RequestedScreenSize
    {
        get
        {
            // None of these is standard, but between them they cover what the major publishers
            // stamped into their suites, and picking the wrong screen size for a game that ships
            // art for one specific resolution usually means a black screen rather than a scaled
            // one.
            string? canvas =
                Attributes.GetValueOrDefault("Nokia-MIDlet-Original-Display-Size")
                ?? Attributes.GetValueOrDefault("Nokia-MIDlet-Target-Display-Size")
                ?? Attributes.GetValueOrDefault("SEMC-Screen-Size")
                ?? Attributes.GetValueOrDefault("Display-Size")
                ?? Attributes.GetValueOrDefault("MIDlet-ScreenSize")
                ?? Attributes.GetValueOrDefault("MIDxlet-ScreenSize");

            if (string.IsNullOrWhiteSpace(canvas)) return null;

            string[] parts = canvas.Split(',', 'x', 'X');
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[0].Trim(), out int width)) return null;
            if (!int.TryParse(parts[1].Trim(), out int height)) return null;

            return (width, height);
        }
    }

    /// <summary>Opens a suite from a JAR, merging in a JAD beside it if one exists.</summary>
    public static MidletSuite Open(string jarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jarPath);
        if (!File.Exists(jarPath)) throw new FileNotFoundException("Suite JAR not found.", jarPath);

        JarClassSource source = JarClassSource.Open(jarPath);

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source.ReadManifestText() is { } manifest) ParseAttributes(manifest, attributes);

        string jadPath = Path.ChangeExtension(jarPath, ".jad");
        if (File.Exists(jadPath)) ParseAttributes(File.ReadAllText(jadPath), attributes);

        return new MidletSuite(jarPath, source, attributes);
    }

    /// <summary>
    /// Parses manifest or JAD text.
    /// </summary>
    /// <remarks>
    /// Real suites break the format constantly: missing trailing newlines, CRLF mixed with LF,
    /// colons inside values, and continuation lines. Being permissive here is the difference
    /// between a game appearing in the library and being rejected before it ever runs.
    /// </remarks>
    public static void ParseAttributes(string text, Dictionary<string, string> into)
    {
        string? key = null;
        var value = new StringBuilder();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            // A leading space continues the previous value, as the manifest format specifies.
            if (line[0] is ' ' or '\t')
            {
                if (key is not null) value.Append(line.TrimStart());
                continue;
            }

            if (key is not null) into[key] = value.ToString().Trim();

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                key = null;
                continue;
            }

            key = line[..separator].Trim();
            value.Clear().Append(line[(separator + 1)..].Trim());
        }

        if (key is not null) into[key] = value.ToString().Trim();
    }

    private static IReadOnlyList<MidletEntry> ParseMidlets(Dictionary<string, string> attributes)
    {
        var entries = new List<MidletEntry>();

        for (int index = 1; ; index++)
        {
            if (!attributes.TryGetValue($"MIDlet-{index}", out string? line)) break;

            // The value is "name, icon, class"; the icon is frequently empty.
            string[] parts = line.Split(',');
            if (parts.Length < 3) continue;

            string name = parts[0].Trim();
            string icon = parts[1].Trim();
            string className = parts[^1].Trim().Replace('.', '/');
            if (className.Length == 0) continue;

            entries.Add(new MidletEntry(
                name.Length == 0 ? className : name,
                icon.Length == 0 ? null : icon,
                className));
        }

        // A suite with no usable MIDlet-n attributes is not necessarily broken: some were
        // repackaged badly. Fall back to scanning for a class that extends MIDlet.
        if (entries.Count == 0) return [];

        return entries;
    }

    /// <summary>
    /// Scans the JAR for classes that look like MIDlets, used when the manifest declares none.
    /// </summary>
    /// <remarks>
    /// This inspects the constant pool for a reference to <c>javax/microedition/midlet/MIDlet</c>
    /// rather than loading the class, so a suite with hundreds of classes is scanned in
    /// milliseconds and a class that fails to load cannot break discovery.
    /// </remarks>
    public IReadOnlyList<string> DiscoverMidletClasses()
    {
        var found = new List<string>();
        ReadOnlySpan<byte> marker = "javax/microedition/midlet/MIDlet"u8;

        foreach (string entry in Source.EnumerateEntries())
        {
            if (!entry.EndsWith(".class", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Source.TryGetResource(entry, out byte[] bytes)) continue;

            if (bytes.AsSpan().IndexOf(marker) >= 0)
                found.Add(entry[..^".class".Length]);
        }

        return found;
    }
}
