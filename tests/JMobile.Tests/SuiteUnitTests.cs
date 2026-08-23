using JMobile.Emulator.Suites;
using JMobile.Runtime.Host;

namespace JMobile.Tests;

/// <summary>Tests for suite metadata parsing and the device profile key mapping.</summary>
public static class SuiteUnitTests
{
    public static void Register()
    {
        RegisterManifestTests();
        RegisterDeviceTests();
        RegisterStorageTests();
    }

    private static void RegisterManifestTests()
    {
        Test.Suite("manifest parsing");

        Test.Case("reads simple attributes", () =>
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MidletSuite.ParseAttributes(
                "MIDlet-Name: Space Game\nMIDlet-Vendor: Example Ltd\nMIDlet-Version: 1.2.3\n",
                attributes);

            Test.Equal("Space Game", attributes["MIDlet-Name"]);
            Test.Equal("Example Ltd", attributes["MIDlet-Vendor"]);
            Test.Equal("1.2.3", attributes["MIDlet-Version"]);
        });

        Test.Case("attribute names are case-insensitive", () =>
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MidletSuite.ParseAttributes("midlet-name: Lower\n", attributes);
            Test.Equal("Lower", attributes["MIDlet-Name"]);
        });

        Test.Case("continuation lines are joined", () =>
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MidletSuite.ParseAttributes("MIDlet-Description: A very long\n description that wraps\n", attributes);

            Test.Equal("A very longdescription that wraps", attributes["MIDlet-Description"],
                "the manifest format joins continuations without adding a space");
        });

        Test.Case("values may contain colons", () =>
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MidletSuite.ParseAttributes("MIDlet-Info-URL: http://example.com:8080/info\n", attributes);
            Test.Equal("http://example.com:8080/info", attributes["MIDlet-Info-URL"]);
        });

        Test.Case("CRLF and a missing final newline are tolerated", () =>
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MidletSuite.ParseAttributes("MIDlet-Name: Windows\r\nMIDlet-Vendor: NoNewline", attributes);

            Test.Equal("Windows", attributes["MIDlet-Name"]);
            Test.Equal("NoNewline", attributes["MIDlet-Vendor"], "the last line still counts");
        });

        Test.Case("malformed lines are skipped rather than fatal", () =>
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MidletSuite.ParseAttributes("garbage line\nMIDlet-Name: Survivor\n", attributes);
            Test.Equal("Survivor", attributes["MIDlet-Name"]);
        });
    }

    private static void RegisterDeviceTests()
    {
        Test.Suite("device profiles");

        DeviceProfile device = DeviceProfile.Series60;

        Test.Case("navigation keys map to game actions", () =>
        {
            Test.Equal(GameAction.Up, device.GetGameAction(KeyCodes.Up));
            Test.Equal(GameAction.Fire, device.GetGameAction(KeyCodes.Fire));
            Test.Equal(GameAction.Left, device.GetGameAction(KeyCodes.Left));
        });

        Test.Case("the number pad doubles as a d-pad", () =>
        {
            Test.Equal(GameAction.Up, device.GetGameAction(KeyCodes.Num2));
            Test.Equal(GameAction.Down, device.GetGameAction(KeyCodes.Num8));
            Test.Equal(GameAction.Left, device.GetGameAction(KeyCodes.Num4));
            Test.Equal(GameAction.Right, device.GetGameAction(KeyCodes.Num6));
            Test.Equal(GameAction.Fire, device.GetGameAction(KeyCodes.Num5));
        });

        Test.Case("unmapped keys report no action", () =>
            Test.Equal(0, device.GetGameAction(KeyCodes.Star)));

        Test.Case("key state bits match the GameCanvas constants", () =>
        {
            Test.Equal(0x0002, DeviceProfile.GetKeyStateBit(GameAction.Up));
            Test.Equal(0x0004, DeviceProfile.GetKeyStateBit(GameAction.Left));
            Test.Equal(0x0020, DeviceProfile.GetKeyStateBit(GameAction.Right));
            Test.Equal(0x0040, DeviceProfile.GetKeyStateBit(GameAction.Down));
            Test.Equal(0x0100, DeviceProfile.GetKeyStateBit(GameAction.Fire));
        });

        Test.Case("game actions round-trip through key codes", () =>
        {
            foreach (int action in new[]
                     {
                         GameAction.Up, GameAction.Down, GameAction.Left,
                         GameAction.Right, GameAction.Fire,
                     })
            {
                int keyCode = device.GetKeyCode(action);
                Test.Equal(action, device.GetGameAction(keyCode), $"action {action}");
            }
        });

        Test.Case("key names are readable", () =>
        {
            Test.Equal("Up", device.GetKeyName(KeyCodes.Up));
            Test.Equal("5", device.GetKeyName(KeyCodes.Num5));
        });
    }

    private static void RegisterStorageTests()
    {
        Test.Suite("record storage");

        Test.Case("stores round-trip through the file host", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "jmobile-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new FileStorageHost(root);

                Test.False(storage.TryRead("save", out _), "nothing is stored yet");

                storage.Write("save", [1, 2, 3, 4]);
                Test.True(storage.TryRead("save", out byte[] data), "the store now exists");
                Test.Equal(4, data.Length);
                Test.Equal((byte)3, data[2]);

                Test.True(storage.List().Contains("save"), "the store is listed");

                storage.Delete("save");
                Test.False(storage.TryRead("save", out _), "the store is gone");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });

        Test.Case("awkward store names survive the file system", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "jmobile-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new FileStorageHost(root);
                // Games choose these names, and nothing stops them containing path separators.
                const string awkward = "save/slot:1*";

                storage.Write(awkward, [9]);
                Test.True(storage.TryRead(awkward, out byte[] data), "read back by the same name");
                Test.Equal((byte)9, data[0]);
                Test.True(storage.List().Contains(awkward), "the original name is recovered");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });
    }
}
