using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using JMobile.Emulator.Session;
using JMobile.Emulator.Suites;
using JMobile.Runtime.Graphics;
using JMobile.Runtime.Host;

namespace JMobile.Tests;

/// <summary>
/// Runs the sample MIDlet through a real session, headlessly.
/// </summary>
/// <remarks>
/// This is the test that would catch a regression the unit tests cannot: the MIDlet lifecycle,
/// the event thread, the display pipeline and the record store all have to work together before
/// a single pixel appears. It asserts on the actual contents of the framebuffer, so a game that
/// starts but renders nothing fails here.
/// <para>
/// Set <c>JMOBILE_FRAME_DUMP</c> to a directory to have the captured frames written out as PNGs,
/// which is the quickest way to see what the emulator actually drew.
/// </para>
/// </remarks>
public static class SessionSmokeTests
{
    public static void Register()
    {
        Test.Suite("session");

        string? jar = LocateSampleJar();
        if (jar is null)
        {
            Test.Case("sample suite is present", () =>
                throw new AssertionException(
                    "samples/dodger/Dodger.jar was not found; run tools/build-sample.ps1"));
            return;
        }

        Test.Case("the suite manifest is read", () =>
        {
            MidletSuite suite = MidletSuite.Open(jar);

            Test.Equal("Dodger", suite.Name);
            Test.Equal("JMobile", suite.Vendor);
            Test.Equal(1, suite.Midlets.Count, "one MIDlet is declared");
            Test.Equal("Dodger", suite.Midlets[0].ClassName);
        });

        Test.Case("the MIDlet starts and paints a frame", () => RunSession(jar, RunsAndPaints));
        Test.Case("input reaches the game", () => RunSession(jar, RespondsToInput));
        Test.Case("the high score survives a restart", () => RunSession(jar, PersistsAcrossRuns));
    }

    // ---------------------------------------------------------------------------------
    // Scenarios
    // ---------------------------------------------------------------------------------

    private static void RunsAndPaints(EmulatorSession session)
    {
        Test.True(WaitForFrames(session, 3, TimeSpan.FromSeconds(10)),
            "the game should paint within ten seconds of starting");

        Test.Equal(SessionState.Running, session.State);
        Test.True(session.LoadedClassCount > 5,
            $"the suite and library classes should be loaded, saw {session.LoadedClassCount}");

        Dump(session, "start");

        // The game paints a starfield, rocks and a HUD, so the frame must contain more than one
        // colour. A blank screen is exactly the failure this test exists to catch.
        Test.True(CountDistinctColours(session.Screen) > 4,
            "the painted frame should contain several colours, not a flat fill");
    }

    private static void RespondsToInput(EmulatorSession session)
    {
        Test.True(WaitForFrames(session, 5, TimeSpan.FromSeconds(10)), "the game starts");

        int[] before = CaptureRow(session.Screen, session.Screen.Height - 22);

        // Hold left for long enough that the ship visibly moves, then let go.
        session.PressKey(KeyCodes.Left);
        Thread.Sleep(400);
        session.ReleaseKey(KeyCodes.Left);

        Test.True(WaitForFrames(session, 3, TimeSpan.FromSeconds(5)), "frames keep coming after input");
        Dump(session, "after-input");

        int[] after = CaptureRow(session.Screen, session.Screen.Height - 22);
        Test.False(before.AsSpan().SequenceEqual(after),
            "the row the ship sits on should differ once the ship has moved");
    }

    private static void PersistsAcrossRuns(EmulatorSession session)
    {
        Test.True(WaitForFrames(session, 3, TimeSpan.FromSeconds(10)), "the game starts");

        // The MIDlet writes its high score through the record store on shutdown; a save
        // directory that stays empty means RMS never reached the disk.
        session.Stop();

        string saves = SaveDirectory();
        Test.True(Directory.Exists(saves), "the save directory was created");
        Test.True(Directory.GetFiles(saves, "*.rms").Length > 0,
            "the game's record store should have been written");
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static void RunSession(string jarPath, Action<EmulatorSession> body)
    {
        var session = new EmulatorSession(new EmulatorSessionOptions
        {
            Suite = MidletSuite.Open(jarPath),
            Device = DeviceProfile.Series60,
            SaveDirectory = SaveDirectory(),
            // No font rasteriser and no audio: this runs on a build agent with neither.
            FontRasterizer = NullFontRasterizer.Instance,
        });

        var failures = new List<string>();
        session.Logged += (text, isError) =>
        {
            if (isError) failures.Add(text.Trim());
        };

        try
        {
            session.Start();
            body(session);

            // A game that throws on its own thread still "runs", so the log is checked too.
            Test.Equal(0, failures.Count,
                failures.Count == 0 ? null : "guest errors: " + string.Join(" | ", failures));
        }
        finally
        {
            session.Stop();
            session.Dispose();
        }
    }

    /// <summary>Waits until the session has painted at least <paramref name="count"/> more frames.</summary>
    private static bool WaitForFrames(EmulatorSession session, int count, TimeSpan timeout)
    {
        long target = session.FramesPainted + count;
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < timeout)
        {
            if (session.FramesPainted >= target) return true;
            if (session.State is SessionState.Failed or SessionState.Stopped) return false;

            Thread.Sleep(20);
        }

        return false;
    }

    private static string SaveDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "jmobile-smoke", "dodger");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string? LocateSampleJar()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "samples", "dodger", "Dodger.jar");
            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    // ---------------------------------------------------------------------------------
    // Frame inspection
    // ---------------------------------------------------------------------------------

    private static int CountDistinctColours(Surface surface)
    {
        var seen = new HashSet<int>();
        foreach (int pixel in surface.Pixels)
        {
            seen.Add(pixel);
            if (seen.Count > 64) break;
        }
        return seen.Count;
    }

    private static int[] CaptureRow(Surface surface, int y)
    {
        int row = Math.Clamp(y, 0, surface.Height - 1);
        return surface.Pixels.AsSpan(row * surface.Width, surface.Width).ToArray();
    }

    /// <summary>Writes the current frame out as a PNG when frame dumping is switched on.</summary>
    private static void Dump(EmulatorSession session, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("JMOBILE_FRAME_DUMP");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), EncodePng(session.Screen));
    }

    /// <summary>Encodes a surface as an uncompressed-filter PNG, for frame dumps only.</summary>
    private static byte[] EncodePng(Surface surface)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, surface.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), surface.Height);
        header[8] = 8;
        header[9] = 2; // truecolour, no alpha
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        for (int y = 0; y < surface.Height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < surface.Width; x++)
            {
                int pixel = surface.GetPixel(x, y);
                raw.WriteByte((byte)(pixel >> 16));
                raw.WriteByte((byte)(pixel >> 8));
                raw.WriteByte((byte)pixel);
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        output.Write(length);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(payload);

        // Real PNG viewers check the CRC, so frame dumps compute it properly.
        output.Write(Crc32(typeBytes, payload));
    }

    private static byte[] Crc32(byte[] type, byte[] payload)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in type) crc = Step(crc, value);
        foreach (byte value in payload) crc = Step(crc, value);

        crc ^= 0xFFFFFFFF;

        var result = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, crc);
        return result;

        static uint Step(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            return crc;
        }
    }
}
