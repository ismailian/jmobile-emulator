namespace JMobile.Runtime.Host;

/// <summary>
/// Everything the runtime library needs from the machine it is running on that is not pixels.
/// </summary>
/// <remarks>
/// Each contract is separate rather than bundled into one host interface, so a caller that only
/// needs storage — a test, a headless conformance run — implements storage alone.
/// </remarks>
public interface IAudioHost
{
    /// <summary>Plays a single tone, backing <c>Manager.playTone</c>.</summary>
    void PlayTone(int note, int durationMilliseconds, int volume);

    /// <summary>Prepares a clip from encoded media, or returns null if the format is unsupported.</summary>
    IAudioClip? CreateClip(byte[] data, string mimeType);

    /// <summary>Stops everything currently sounding, used when a game is paused or closed.</summary>
    void StopAll();
}

/// <summary>A prepared piece of audio a MIDlet can start, stop and loop.</summary>
public interface IAudioClip : IDisposable
{
    /// <summary>Total length in microseconds, or -1 when unknown.</summary>
    long Duration { get; }

    bool IsPlaying { get; }

    /// <summary>Plays the clip, repeating it the given number of times (0 or 1 plays once).</summary>
    void Play(int loopCount);

    void Stop();

    /// <summary>Volume as a percentage from 0 to 100, matching the MIDP volume control.</summary>
    int Volume { get; set; }
}

/// <summary>Persistence for the record management system and for anything else that outlives a run.</summary>
public interface IStorageHost
{
    bool TryRead(string storeName, out byte[] data);

    void Write(string storeName, ReadOnlySpan<byte> data);

    void Delete(string storeName);

    IReadOnlyList<string> List();
}

/// <summary>
/// Outbound network access for the Generic Connection Framework.
/// </summary>
/// <remarks>
/// Kept behind an interface primarily so that it can be denied. A twenty-year-old game
/// reaching for a server that no longer exists should fail cleanly and instantly rather than
/// stalling the emulator on a DNS timeout, and the host decides which is which.
/// </remarks>
public interface INetworkHost
{
    bool IsEnabled { get; }

    /// <summary>Performs an HTTP request synchronously, as the blocking MIDP API expects.</summary>
    HttpResult Request(HttpRequest request);
}

/// <param name="Url">Absolute request URL.</param>
/// <param name="Method">GET, POST or HEAD.</param>
/// <param name="Headers">Request headers set by the MIDlet.</param>
/// <param name="Body">Request body, empty for GET and HEAD.</param>
public readonly record struct HttpRequest(
    string Url,
    string Method,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body);

/// <param name="StatusCode">HTTP status, or 0 when the request could not be made.</param>
/// <param name="StatusMessage">Reason phrase or error description.</param>
/// <param name="Headers">Response headers.</param>
/// <param name="Body">Response body.</param>
public readonly record struct HttpResult(
    int StatusCode,
    string StatusMessage,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body);

/// <summary>Feedback devices that have no other home: vibration and the backlight.</summary>
public interface IDeviceFeedbackHost
{
    void Vibrate(int durationMilliseconds);

    void FlashBacklight(int durationMilliseconds);
}

/// <summary>
/// Storage that keeps each suite's record stores in its own folder on disk.
/// </summary>
public sealed class FileStorageHost : IStorageHost
{
    private readonly string _root;
    private readonly Lock _gate = new();

    public FileStorageHost(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public bool TryRead(string storeName, out byte[] data)
    {
        lock (_gate)
        {
            string path = PathFor(storeName);
            if (!File.Exists(path))
            {
                data = [];
                return false;
            }

            data = File.ReadAllBytes(path);
            return true;
        }
    }

    public void Write(string storeName, ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            string path = PathFor(storeName);
            string temporary = path + ".tmp";

            // Written aside and moved into place, so a crash mid-save cannot leave a game with
            // a half-written save file where a missing one would have been recoverable.
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
    }

    public void Delete(string storeName)
    {
        lock (_gate)
        {
            string path = PathFor(storeName);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public IReadOnlyList<string> List()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_root)) return [];

            return Directory.EnumerateFiles(_root, "*.rms")
                .Select(file => Decode(Path.GetFileNameWithoutExtension(file)))
                .ToArray();
        }
    }

    /// <summary>
    /// Record store names are chosen by the game and may contain characters a file system will
    /// not accept, so they are percent-encoded rather than sanitised, which would collide.
    /// </summary>
    private string PathFor(string storeName) => Path.Combine(_root, Encode(storeName) + ".rms");

    private static string Encode(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);
        foreach (char character in name)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                builder.Append(character);
            else
                builder.Append('%').Append(((int)character).ToString("X4"));
        }
        return builder.ToString();
    }

    private static string Decode(string encoded)
    {
        var builder = new System.Text.StringBuilder(encoded.Length);
        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '%' && i + 4 < encoded.Length)
            {
                builder.Append((char)Convert.ToInt32(encoded.Substring(i + 1, 4), 16));
                i += 4;
            }
            else
            {
                builder.Append(encoded[i]);
            }
        }
        return builder.ToString();
    }
}
