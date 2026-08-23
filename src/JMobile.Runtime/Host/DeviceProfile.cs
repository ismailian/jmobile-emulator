namespace JMobile.Runtime.Host;

/// <summary>The nine abstract inputs MIDP defines on top of physical keys.</summary>
public static class GameAction
{
    public const int Up = 1;
    public const int Left = 2;
    public const int Right = 5;
    public const int Down = 6;
    public const int Fire = 8;
    public const int GameA = 9;
    public const int GameB = 10;
    public const int GameC = 11;
    public const int GameD = 12;
}

/// <summary>Key codes MIDP names explicitly, plus the de-facto handset codes games rely on.</summary>
public static class KeyCodes
{
    public const int Num0 = '0';
    public const int Num1 = '1';
    public const int Num2 = '2';
    public const int Num3 = '3';
    public const int Num4 = '4';
    public const int Num5 = '5';
    public const int Num6 = '6';
    public const int Num7 = '7';
    public const int Num8 = '8';
    public const int Num9 = '9';
    public const int Star = '*';
    public const int Pound = '#';

    // MIDP never standardised the navigation cluster, but essentially every handset that
    // mattered used these negative codes, and a great many games hard-code them.
    public const int Up = -1;
    public const int Down = -2;
    public const int Left = -3;
    public const int Right = -4;
    public const int Fire = -5;
    public const int SoftLeft = -6;
    public const int SoftRight = -7;
    public const int Clear = -8;
    public const int Send = -10;
    public const int End = -11;
}

/// <summary>
/// The handset a suite believes it is running on.
/// </summary>
/// <remarks>
/// J2ME games were written against specific phones, and many of them read
/// <c>microedition.platform</c> or check the screen size to pick asset sets and control
/// schemes. Emulating a plausible handset rather than an abstract one is what makes those
/// games take the code path they were tested on.
/// </remarks>
public sealed class DeviceProfile
{
    public required string Name { get; init; }

    public required int ScreenWidth { get; init; }

    public required int ScreenHeight { get; init; }

    /// <summary>Bits per channel the display can show; games use this to pick asset variants.</summary>
    public int ColorDepth { get; init; } = 8;

    public bool IsColor { get; init; } = true;

    public bool HasPointerEvents { get; init; }

    public bool HasRepeatEvents { get; init; } = true;

    /// <summary>Value reported for the <c>microedition.platform</c> system property.</summary>
    public string Platform { get; init; } = "j2me";

    public string Encoding { get; init; } = "ISO-8859-1";

    public string Locale { get; init; } = "en-US";

    /// <summary>Heap ceiling reported by <c>Runtime.totalMemory</c>, in bytes.</summary>
    public long HeapSize { get; init; } = 2 * 1024 * 1024;

    /// <summary>Vendor-specific extension APIs to expose, such as the Nokia UI package.</summary>
    public bool EnableNokiaUi { get; init; } = true;

    /// <summary>Number of pixels reserved at the bottom for soft-key labels, if any.</summary>
    public int SoftKeyBarHeight { get; init; }

    /// <summary>Maps a physical key code to the abstract action it stands for.</summary>
    public int GetGameAction(int keyCode) => keyCode switch
    {
        KeyCodes.Up or KeyCodes.Num2 => GameAction.Up,
        KeyCodes.Down or KeyCodes.Num8 => GameAction.Down,
        KeyCodes.Left or KeyCodes.Num4 => GameAction.Left,
        KeyCodes.Right or KeyCodes.Num6 => GameAction.Right,
        KeyCodes.Fire or KeyCodes.Num5 => GameAction.Fire,
        KeyCodes.Num1 => GameAction.GameA,
        KeyCodes.Num3 => GameAction.GameB,
        KeyCodes.Num7 => GameAction.GameC,
        KeyCodes.Num9 => GameAction.GameD,
        _ => 0,
    };

    /// <summary>Maps an abstract action back to the key code this handset reports for it.</summary>
    public int GetKeyCode(int gameAction) => gameAction switch
    {
        GameAction.Up => KeyCodes.Up,
        GameAction.Down => KeyCodes.Down,
        GameAction.Left => KeyCodes.Left,
        GameAction.Right => KeyCodes.Right,
        GameAction.Fire => KeyCodes.Fire,
        GameAction.GameA => KeyCodes.Num1,
        GameAction.GameB => KeyCodes.Num3,
        GameAction.GameC => KeyCodes.Num7,
        GameAction.GameD => KeyCodes.Num9,
        _ => 0,
    };

    /// <summary>The name a game sees from <c>Canvas.getKeyName</c>.</summary>
    public string GetKeyName(int keyCode) => keyCode switch
    {
        KeyCodes.Up => "Up",
        KeyCodes.Down => "Down",
        KeyCodes.Left => "Left",
        KeyCodes.Right => "Right",
        KeyCodes.Fire => "Select",
        KeyCodes.SoftLeft => "Soft1",
        KeyCodes.SoftRight => "Soft2",
        KeyCodes.Clear => "Clear",
        >= 32 and < 127 => ((char)keyCode).ToString(),
        _ => "Key" + keyCode,
    };

    /// <summary>
    /// The bit <c>GameCanvas.getKeyStates</c> sets for an action. These constants are fixed by
    /// the specification, so they are derived from the action rather than from the key.
    /// </summary>
    public static int GetKeyStateBit(int gameAction) => gameAction switch
    {
        GameAction.Up => 0x0002,
        GameAction.Left => 0x0004,
        GameAction.Right => 0x0020,
        GameAction.Down => 0x0040,
        GameAction.Fire => 0x0100,
        GameAction.GameA => 0x0200,
        GameAction.GameB => 0x0400,
        GameAction.GameC => 0x0800,
        GameAction.GameD => 0x1000,
        _ => 0,
    };

    // -- stock handsets ----------------------------------------------------------------

    /// <summary>176x208, the Series 60 screen an enormous share of J2ME games targeted.</summary>
    public static DeviceProfile Series60 => new()
    {
        Name = "Generic 176x208 (Series 60)",
        ScreenWidth = 176,
        ScreenHeight = 208,
        Platform = "Nokia6600/4.09",
        ColorDepth = 8,
    };

    /// <summary>128x128, the small screen of the earliest colour MIDP handsets.</summary>
    public static DeviceProfile Small128 => new()
    {
        Name = "Generic 128x128",
        ScreenWidth = 128,
        ScreenHeight = 128,
        Platform = "Nokia3510i/1.0",
        ColorDepth = 4,
    };

    /// <summary>240x320, the later QVGA standard.</summary>
    public static DeviceProfile Qvga => new()
    {
        Name = "Generic 240x320 (QVGA)",
        ScreenWidth = 240,
        ScreenHeight = 320,
        Platform = "SonyEricssonK800i/R1A",
        ColorDepth = 8,
    };

    /// <summary>240x320 with a touchscreen, for the last generation of MIDP devices.</summary>
    public static DeviceProfile QvgaTouch => new()
    {
        Name = "Generic 240x320 Touch",
        ScreenWidth = 240,
        ScreenHeight = 320,
        Platform = "Nokia5800d-1/40.0",
        ColorDepth = 8,
        HasPointerEvents = true,
    };

    /// <summary>128x160, the other common small screen alongside 128x128.</summary>
    public static DeviceProfile Small128x160 => new()
    {
        Name = "Generic 128x160",
        ScreenWidth = 128,
        ScreenHeight = 160,
        Platform = "Nokia6230i/2.0",
        ColorDepth = 6,
    };

    /// <summary>176x220, the Series 40 screen, as widely targeted as Series 60's 176x208.</summary>
    public static DeviceProfile Series40 => new()
    {
        Name = "Generic 176x220 (Series 40)",
        ScreenWidth = 176,
        ScreenHeight = 220,
        Platform = "Nokia6300/5.0",
        ColorDepth = 8,
    };

    /// <summary>240x400, the taller QVGA variant used by later Samsung and LG handsets.</summary>
    public static DeviceProfile Tall240 => new()
    {
        Name = "Generic 240x400",
        ScreenWidth = 240,
        ScreenHeight = 400,
        Platform = "SAMSUNG-GT-S5230",
        ColorDepth = 8,
        HasPointerEvents = true,
    };

    /// <summary>320x240 landscape, which sliders and gaming handsets used.</summary>
    public static DeviceProfile Landscape320 => new()
    {
        Name = "Generic 320x240 (landscape)",
        ScreenWidth = 320,
        ScreenHeight = 240,
        Platform = "NokiaE71-1/100",
        ColorDepth = 8,
    };

    /// <summary>360x640, the last widely targeted MIDP resolution.</summary>
    public static DeviceProfile Wvga360 => new()
    {
        Name = "Generic 360x640",
        ScreenWidth = 360,
        ScreenHeight = 640,
        Platform = "Nokia5230/20.0",
        ColorDepth = 8,
        HasPointerEvents = true,
        HeapSize = 8 * 1024 * 1024,
    };

    /// <summary>
    /// 480x800, which the last generation of Java games was built for.
    /// </summary>
    /// <remarks>
    /// Late Gameloft and EA titles ship art for this size and declare it through
    /// <c>SEMC-Screen-Size</c>. Running one at 176x208 usually produces a black screen rather
    /// than a scaled-down game, because the art is simply drawn off the visible area.
    /// </remarks>
    public static DeviceProfile Wvga480 => new()
    {
        Name = "Generic 480x800",
        ScreenWidth = 480,
        ScreenHeight = 800,
        Platform = "SonyEricssonU5i/R1FA",
        ColorDepth = 8,
        HasPointerEvents = true,
        HeapSize = 16 * 1024 * 1024,
    };

    /// <summary>Every built-in profile, smallest first.</summary>
    public static IReadOnlyList<DeviceProfile> BuiltIn { get; } =
    [
        Small128,
        Small128x160,
        Series60,
        Series40,
        Qvga,
        QvgaTouch,
        Landscape320,
        Tall240,
        Wvga360,
        Wvga480,
    ];

    /// <summary>
    /// Finds the built-in profile closest to a size a suite asked for.
    /// </summary>
    /// <remarks>
    /// An exact match is preferred. Failing that the smallest profile that still fits the
    /// requested art is chosen, because a game drawing into a larger screen leaves borders,
    /// while one drawing into a smaller screen loses whatever falls outside it.
    /// </remarks>
    public static DeviceProfile? ClosestTo(int width, int height)
    {
        DeviceProfile? best = null;
        long bestScore = long.MaxValue;

        foreach (DeviceProfile profile in BuiltIn)
        {
            if (profile.ScreenWidth == width && profile.ScreenHeight == height) return profile;
            if (profile.ScreenWidth < width || profile.ScreenHeight < height) continue;

            long score = ((long)profile.ScreenWidth * profile.ScreenHeight) - ((long)width * height);
            if (score >= bestScore) continue;

            bestScore = score;
            best = profile;
        }

        return best;
    }
}
