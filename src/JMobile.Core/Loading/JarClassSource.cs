using System.IO.Compression;
using System.Text;

namespace JMobile.Core.Loading;

/// <summary>
/// Serves classes and resources out of a MIDlet suite JAR.
/// </summary>
/// <remarks>
/// The archive is fully decompressed into memory when the source is created rather than being
/// read entry by entry on demand. Suites are small, a megabyte or two at most, while
/// <see cref="ZipArchive"/> is neither thread-safe nor seek-friendly and games load resources
/// from whichever thread happens to need them. Paying the decompression cost once at load time
/// removes both the locking and the stutter mid-game.
/// </remarks>
public sealed class JarClassSource : IClassSource
{
    private readonly Dictionary<string, byte[]> _entries;

    private JarClassSource(string name, Dictionary<string, byte[]> entries)
    {
        Name = name;
        _entries = entries;
    }

    public string Name { get; }

    /// <summary>Raw bytes of <c>META-INF/MANIFEST.MF</c>, or null when the JAR has no manifest.</summary>
    public byte[]? ManifestBytes { get; private init; }

    /// <summary>Total uncompressed size of the suite, reported in the library UI.</summary>
    public long UncompressedSize { get; private init; }

    public static JarClassSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Open(stream, Path.GetFileName(path));
    }

    public static JarClassSource Open(Stream stream, string name)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? manifest = null;
        long total = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                // Directory entries have an empty name and no content.
                if (entry.FullName.EndsWith('/')) continue;

                byte[] data = ReadFully(entry);
                string key = Normalize(entry.FullName);
                entries[key] = data;
                total += data.Length;

                if (string.Equals(key, "META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase))
                    manifest = data;
            }
        }

        return new JarClassSource(name, entries)
        {
            ManifestBytes = manifest,
            UncompressedSize = total,
        };
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        // Length is the uncompressed size, so the buffer is exact and never regrows.
        var buffer = new byte[entry.Length];
        using Stream source = entry.Open();

        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = source.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) break;
            offset += read;
        }

        return offset == buffer.Length ? buffer : buffer[..offset];
    }

    public bool TryGetClassBytes(string internalName, out byte[] bytes) =>
        _entries.TryGetValue(internalName + ".class", out bytes!);

    public bool TryGetResource(string path, out byte[] bytes)
    {
        string key = Normalize(path);
        if (_entries.TryGetValue(key, out bytes!)) return true;

        // Suites built on Windows toolchains occasionally differ from the lookup only by case.
        foreach (KeyValuePair<string, byte[]> entry in _entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                bytes = entry.Value;
                return true;
            }
        }

        bytes = [];
        return false;
    }

    public IEnumerable<string> EnumerateEntries() => _entries.Keys;

    /// <summary>Reads the manifest as text, tolerating the encodings suites are found with.</summary>
    public string? ReadManifestText() =>
        ManifestBytes is null ? null : Encoding.UTF8.GetString(ManifestBytes);

    private static string Normalize(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Length > 0 && normalized[0] == '/' ? normalized[1..] : normalized;
    }
}
