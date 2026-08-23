using System.Buffers.Binary;
using System.Runtime.InteropServices;
using JMobile.Runtime.Host;

namespace JMobile.App.Hosting;

/// <summary>
/// Audio output built directly on the Windows waveform API.
/// </summary>
/// <remarks>
/// Built on <c>waveOut</c> rather than <see cref="System.Media.SoundPlayer"/> because the
/// simpler API can only have one sound in flight for the whole process: starting a sound effect
/// would cut the music. Each clip here owns its own device handle, so a game can have several
/// voices at once, and the API also exposes the volume control MIDP's <c>VolumeControl</c> needs.
/// </remarks>
public sealed class WinMmAudioHost : IAudioHost, IDisposable
{
    private readonly List<WaveClip> _clips = [];
    private readonly Lock _gate = new();
    private bool _enabled = true;
    private int _masterVolume = 70;

    /// <summary>Turns all output on or off without the game noticing.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) StopAll();
        }
    }

    /// <summary>Master volume percentage applied on top of each clip's own level.</summary>
    public int MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0, 100);
    }

    public void PlayTone(int note, int durationMilliseconds, int volume)
    {
        if (!_enabled || durationMilliseconds <= 0) return;

        byte[] wave = ToneGenerator.Generate(note, durationMilliseconds);
        IAudioClip? clip = CreateClip(wave, "audio/x-wav");
        if (clip is null) return;

        clip.Volume = volume;
        clip.Play(1);

        // A tone is fire-and-forget, so it disposes itself once it has finished sounding.
        _ = Task.Delay(durationMilliseconds + 200).ContinueWith(_ => clip.Dispose(),
            TaskScheduler.Default);
    }

    public IAudioClip? CreateClip(byte[] data, string mimeType)
    {
        if (!_enabled || data.Length == 0) return null;

        if (!WaveFile.TryParse(data, out WaveFormat format, out byte[] samples))
        {
            // MIDI and the tone-sequence format are not synthesised here. Returning null lets
            // the game carry on silently rather than failing to create a player at all.
            return null;
        }

        var clip = new WaveClip(format, samples, this);
        lock (_gate)
        {
            _clips.Add(clip);
        }
        return clip;
    }

    public void StopAll()
    {
        WaveClip[] snapshot;
        lock (_gate)
        {
            snapshot = _clips.ToArray();
        }

        foreach (WaveClip clip in snapshot) clip.Stop();
    }

    internal void Forget(WaveClip clip)
    {
        lock (_gate)
        {
            _clips.Remove(clip);
        }
    }

    internal int EffectiveVolume(int clipVolume) => clipVolume * _masterVolume / 100;

    public void Dispose()
    {
        WaveClip[] snapshot;
        lock (_gate)
        {
            snapshot = _clips.ToArray();
            _clips.Clear();
        }

        foreach (WaveClip clip in snapshot) clip.Dispose();
    }
}

/// <summary>PCM format description, mirroring the fields <c>waveOutOpen</c> needs.</summary>
public readonly record struct WaveFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public int BlockAlign => Channels * (BitsPerSample / 8);

    public int BytesPerSecond => SampleRate * BlockAlign;
}

/// <summary>One sound, holding its own output device so voices can overlap.</summary>
internal sealed class WaveClip : IAudioClip
{
    private readonly WaveFormat _format;
    private readonly byte[] _samples;
    private readonly WinMmAudioHost _owner;
    private readonly Lock _gate = new();

    private nint _device;
    private GCHandle _pinnedSamples;
    private nint _headerBuffer;
    private int _volume = 100;
    private Thread? _watcher;

    public WaveClip(WaveFormat format, byte[] samples, WinMmAudioHost owner)
    {
        _format = format;
        _samples = samples;
        _owner = owner;
    }

    public long Duration => _format.BytesPerSecond == 0
        ? -1
        : (long)_samples.Length * 1_000_000 / _format.BytesPerSecond;

    public bool IsPlaying { get; private set; }

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            ApplyVolume();
        }
    }

    public void Play(int loopCount)
    {
        Stop();

        lock (_gate)
        {
            var format = new NativeMethods.WaveFormatEx
            {
                FormatTag = 1, // PCM
                Channels = (short)_format.Channels,
                SamplesPerSecond = _format.SampleRate,
                AverageBytesPerSecond = _format.BytesPerSecond,
                BlockAlign = (short)_format.BlockAlign,
                BitsPerSample = (short)_format.BitsPerSample,
                Size = 0,
            };

            if (NativeMethods.waveOutOpen(out _device, NativeMethods.WaveMapper, ref format, 0, 0, 0) != 0)
            {
                _device = 0;
                return;
            }

            ApplyVolume();

            _pinnedSamples = GCHandle.Alloc(_samples, GCHandleType.Pinned);

            var header = new NativeMethods.WaveHeader
            {
                Data = _pinnedSamples.AddrOfPinnedObject(),
                BufferLength = _samples.Length,
                Flags = loopCount > 1
                    ? NativeMethods.WhdrBeginLoop | NativeMethods.WhdrEndLoop
                    : 0,
                Loops = loopCount > 1 ? loopCount : 0,
            };

            _headerBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WaveHeader>());
            Marshal.StructureToPtr(header, _headerBuffer, false);

            int size = Marshal.SizeOf<NativeMethods.WaveHeader>();
            if (NativeMethods.waveOutPrepareHeader(_device, _headerBuffer, size) != 0 ||
                NativeMethods.waveOutWrite(_device, _headerBuffer, size) != 0)
            {
                ReleaseLocked();
                return;
            }

            IsPlaying = true;
        }

        StartWatcher();
    }

    /// <summary>
    /// Watches for the buffer to drain so its resources are released.
    /// </summary>
    /// <remarks>
    /// A polling thread rather than a callback: <c>waveOut</c> callbacks run on a driver thread
    /// with strict rules about which API calls are legal inside them, and getting that wrong
    /// deadlocks the audio device. Polling at fifty-millisecond intervals for the lifetime of a
    /// sound effect costs nothing measurable and cannot deadlock.
    /// </remarks>
    private void StartWatcher()
    {
        _watcher = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(50);

                lock (_gate)
                {
                    if (_device == 0 || _headerBuffer == 0) return;

                    var header = Marshal.PtrToStructure<NativeMethods.WaveHeader>(_headerBuffer);
                    if ((header.Flags & NativeMethods.WhdrDone) == 0) continue;

                    ReleaseLocked();
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "jmobile:audio",
        };

        _watcher.Start();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_device == 0) return;

            NativeMethods.waveOutReset(_device);
            ReleaseLocked();
        }
    }

    private void ApplyVolume()
    {
        if (_device == 0) return;

        int level = _owner.EffectiveVolume(_volume);
        uint channel = (uint)(level * 0xFFFF / 100);
        NativeMethods.waveOutSetVolume(_device, (channel << 16) | channel);
    }

    /// <summary>Frees the device and buffers. The caller must already hold the lock.</summary>
    private void ReleaseLocked()
    {
        if (_headerBuffer != 0)
        {
            NativeMethods.waveOutUnprepareHeader(
                _device, _headerBuffer, Marshal.SizeOf<NativeMethods.WaveHeader>());
            Marshal.FreeHGlobal(_headerBuffer);
            _headerBuffer = 0;
        }

        if (_pinnedSamples.IsAllocated) _pinnedSamples.Free();

        if (_device != 0)
        {
            NativeMethods.waveOutClose(_device);
            _device = 0;
        }

        IsPlaying = false;
    }

    public void Dispose()
    {
        Stop();
        _owner.Forget(this);
    }
}

/// <summary>Reads the PCM payload out of a RIFF/WAVE file.</summary>
internal static class WaveFile
{
    public static bool TryParse(byte[] data, out WaveFormat format, out byte[] samples)
    {
        format = default;
        samples = [];

        if (data.Length < 44) return false;

        var span = new ReadOnlySpan<byte>(data);
        if (span[0] != 'R' || span[1] != 'I' || span[2] != 'F' || span[3] != 'F') return false;
        if (span[8] != 'W' || span[9] != 'A' || span[10] != 'V' || span[11] != 'E') return false;

        int offset = 12;
        bool haveFormat = false;

        while (offset + 8 <= data.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 4)..]);
            int body = offset + 8;
            if (size < 0 || body + size > data.Length) break;

            if (id == "fmt ")
            {
                short channels = BinaryPrimitives.ReadInt16LittleEndian(span[(body + 2)..]);
                int sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span[(body + 4)..]);
                short bits = BinaryPrimitives.ReadInt16LittleEndian(span[(body + 14)..]);

                format = new WaveFormat(sampleRate, Math.Max(1, (int)channels), bits == 0 ? 16 : bits);
                haveFormat = true;
            }
            else if (id == "data")
            {
                samples = span.Slice(body, size).ToArray();
            }

            // Chunks are word-aligned, so an odd size is followed by a pad byte.
            offset = body + size + (size & 1);
        }

        return haveFormat && samples.Length > 0;
    }
}

/// <summary>Synthesises the tones <c>Manager.playTone</c> asks for.</summary>
internal static class ToneGenerator
{
    private const int SampleRate = 22050;

    public static byte[] Generate(int midiNote, int durationMilliseconds)
    {
        // MIDI note 69 is A440; every twelve notes doubles the frequency.
        double frequency = 440.0 * Math.Pow(2.0, (Math.Clamp(midiNote, 0, 127) - 69) / 12.0);
        int sampleCount = Math.Max(1, SampleRate * durationMilliseconds / 1000);

        var output = new byte[44 + (sampleCount * 2)];
        WriteHeader(output, sampleCount);

        // A short fade in and out; a square-edged tone clicks audibly at both ends.
        int fade = Math.Min(sampleCount / 8, SampleRate / 100);

        for (int i = 0; i < sampleCount; i++)
        {
            double envelope = 1.0;
            if (fade > 0)
            {
                if (i < fade) envelope = (double)i / fade;
                else if (i > sampleCount - fade) envelope = (double)(sampleCount - i) / fade;
            }

            double sample = Math.Sin(2.0 * Math.PI * frequency * i / SampleRate) * envelope * 0.35;
            var value = (short)(sample * short.MaxValue);

            output[44 + (i * 2)] = (byte)value;
            output[45 + (i * 2)] = (byte)(value >> 8);
        }

        return output;
    }

    private static void WriteHeader(byte[] output, int sampleCount)
    {
        int dataSize = sampleCount * 2;
        var span = output.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);            // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);            // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);            // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);           // bits per sample
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);
    }
}

internal static partial class NativeMethods
{
    public const int WaveMapper = -1;
    public const int WhdrDone = 0x00000001;
    public const int WhdrBeginLoop = 0x00000004;
    public const int WhdrEndLoop = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormatEx
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSecond;
        public int AverageBytesPerSecond;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHeader
    {
        public nint Data;
        public int BufferLength;
        public int BytesRecorded;
        public nint User;
        public int Flags;
        public int Loops;
        public nint Next;
        public nint Reserved;
    }

    [LibraryImport("winmm.dll")]
    public static partial int waveOutOpen(
        out nint device, int deviceId, ref WaveFormatEx format, nint callback, nint instance, int flags);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutClose(nint device);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutPrepareHeader(nint device, nint header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutUnprepareHeader(nint device, nint header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutWrite(nint device, nint header, int size);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutReset(nint device);

    [LibraryImport("winmm.dll")]
    public static partial int waveOutSetVolume(nint device, uint volume);
}
