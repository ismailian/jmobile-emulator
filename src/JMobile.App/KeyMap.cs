using JMobile.Runtime.Host;

namespace JMobile.App;

/// <summary>
/// Translates desktop key presses into the codes a handset would have reported.
/// </summary>
/// <remarks>
/// Two bindings exist for nearly every handset key: the one a player reaches for instinctively
/// (arrow keys, Enter) and the one that matches the phone's own layout (the number pad as a
/// d-pad). Both are live at once, because games are split on which they read — some call
/// <c>getGameAction</c>, others compare against <c>KEY_NUM2</c> directly.
/// </remarks>
public static class KeyMap
{
    /// <summary>Maps a Windows key to a handset key code, or 0 when it is not bound.</summary>
    public static int ToHandsetKey(Keys key) => key switch
    {
        Keys.Up or Keys.W => KeyCodes.Up,
        Keys.Down or Keys.S => KeyCodes.Down,
        Keys.Left or Keys.A => KeyCodes.Left,
        Keys.Right or Keys.D => KeyCodes.Right,
        Keys.Enter or Keys.Space => KeyCodes.Fire,

        // The soft keys sit under the screen on a handset; Q and E fall under the same fingers.
        Keys.Q or Keys.F1 => KeyCodes.SoftLeft,
        Keys.E or Keys.F2 => KeyCodes.SoftRight,
        Keys.Back => KeyCodes.Clear,

        Keys.D0 or Keys.NumPad0 => KeyCodes.Num0,
        Keys.D1 or Keys.NumPad1 => KeyCodes.Num1,
        Keys.D2 or Keys.NumPad2 => KeyCodes.Num2,
        Keys.D3 or Keys.NumPad3 => KeyCodes.Num3,
        Keys.D4 or Keys.NumPad4 => KeyCodes.Num4,
        Keys.D5 or Keys.NumPad5 => KeyCodes.Num5,
        Keys.D6 or Keys.NumPad6 => KeyCodes.Num6,
        Keys.D7 or Keys.NumPad7 => KeyCodes.Num7,
        Keys.D8 or Keys.NumPad8 => KeyCodes.Num8,
        Keys.D9 or Keys.NumPad9 => KeyCodes.Num9,

        Keys.Multiply or Keys.OemOpenBrackets => KeyCodes.Star,
        Keys.Divide or Keys.OemCloseBrackets => KeyCodes.Pound,

        _ => 0,
    };

    /// <summary>The desktop keys bound to a handset key, for showing hints in the UI.</summary>
    public static string DescribeBinding(int handsetKey) => handsetKey switch
    {
        KeyCodes.Up => "Up / W",
        KeyCodes.Down => "Down / S",
        KeyCodes.Left => "Left / A",
        KeyCodes.Right => "Right / D",
        KeyCodes.Fire => "Enter",
        KeyCodes.SoftLeft => "Q",
        KeyCodes.SoftRight => "E",
        KeyCodes.Clear => "Backspace",
        KeyCodes.Star => "*",
        KeyCodes.Pound => "#",
        >= KeyCodes.Num0 and <= KeyCodes.Num9 => ((char)handsetKey).ToString(),
        _ => "",
    };
}
