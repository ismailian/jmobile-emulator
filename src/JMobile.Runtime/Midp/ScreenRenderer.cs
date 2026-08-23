using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Runtime.Graphics;
using JMobile.Runtime.Host;

namespace JMobile.Runtime.Midp;

/// <summary>
/// Draws and drives the high-level screens, standing in for the part of a handset's UI toolkit
/// that a MIDlet never sees the inside of.
/// </summary>
/// <remarks>
/// A real device rendered <c>List</c>, <c>Form</c>, <c>TextBox</c> and <c>Alert</c> in its own
/// look, and MIDP gives a game no way to customise or even inspect that rendering. This is our
/// look: legible, keyboard-driven and neutral, so a menu built from these classes works and does
/// not pretend to be a specific vendor's skin.
/// </remarks>
public static class ScreenRenderer
{
    private const int Padding = 4;
    private const int TitleBarPadding = 3;

    private const int Background = unchecked((int)0xFF101418);
    private const int Foreground = unchecked((int)0xFFE8ECF0);
    private const int Muted = unchecked((int)0xFF8A94A0);
    private const int TitleBackground = unchecked((int)0xFF1E262E);
    private const int SelectionBackground = unchecked((int)0xFF2E6FA8);
    private const int SelectionForeground = unchecked((int)0xFFFFFFFF);

    // ---------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------

    public static void Render(
        JavaThread thread,
        MidpRuntime runtime,
        JavaObject displayable,
        GraphicsContext context)
    {
        BitmapFont font = runtime.GetFont(0, 0, 0);
        context.Font = font;

        context.SetColor(Background);
        context.FillRect(0, 0, runtime.Screen.Width, runtime.Screen.Height);

        int top = DrawTitle(context, runtime, font, displayable);
        int bottom = DrawSoftKeys(context, runtime, font, displayable);
        int available = bottom - top;

        switch (displayable)
        {
            case JavaChoice choice:
                DrawChoice(context, font, choice, top, available, runtime.Screen.Width);
                break;

            case JavaForm form:
                DrawForm(thread, context, font, form, top, available, runtime.Screen.Width);
                break;

            case JavaTextEntry entry:
                DrawTextEntry(context, font, entry, top, available, runtime.Screen.Width);
                break;

            case JavaAlert alert:
                DrawAlert(context, font, alert, top, available, runtime.Screen.Width);
                break;
        }
    }

    private static int DrawTitle(
        GraphicsContext context,
        MidpRuntime runtime,
        BitmapFont font,
        JavaObject displayable)
    {
        string? title = VirtualMachine.ToHostString(
            displayable.GetField(LcduiModule.TitleSlot).Reference as JavaObject);

        if (string.IsNullOrEmpty(title)) return Padding;

        int height = font.Height + (TitleBarPadding * 2);
        context.SetColor(TitleBackground);
        context.FillRect(0, 0, runtime.Screen.Width, height);

        context.SetColor(Foreground);
        context.DrawString(title, Padding, TitleBarPadding, Anchor.Left | Anchor.Top);

        context.SetColor(Muted);
        context.DrawLine(0, height, runtime.Screen.Width, height);

        return height + Padding;
    }

    /// <summary>
    /// Draws the soft-key labels along the bottom and returns the y the content must stop at.
    /// </summary>
    private static int DrawSoftKeys(
        GraphicsContext context,
        MidpRuntime runtime,
        BitmapFont font,
        JavaObject displayable)
    {
        IReadOnlyList<JavaObject> commands = LcduiModule.CommandsOf(displayable);
        if (commands.Count == 0) return runtime.Screen.Height;

        int barHeight = font.Height + (TitleBarPadding * 2);
        int barTop = runtime.Screen.Height - barHeight;

        context.SetColor(TitleBackground);
        context.FillRect(0, barTop, runtime.Screen.Width, barHeight);
        context.SetColor(Muted);
        context.DrawLine(0, barTop, runtime.Screen.Width, barTop);

        context.SetColor(Foreground);

        string left = LabelOf(commands[0]);
        context.DrawString(left, Padding, barTop + TitleBarPadding, Anchor.Left | Anchor.Top);

        if (commands.Count > 1)
        {
            string right = LabelOf(commands[1]);
            context.DrawString(right, runtime.Screen.Width - Padding, barTop + TitleBarPadding,
                Anchor.Right | Anchor.Top);
        }

        return barTop - Padding;
    }

    private static string LabelOf(JavaObject command) =>
        VirtualMachine.ToHostString(command.GetField(0).Reference as JavaObject) ?? "Select";

    private static void DrawChoice(
        GraphicsContext context,
        BitmapFont font,
        JavaChoice choice,
        int top,
        int available,
        int width)
    {
        if (choice.Texts.Count == 0)
        {
            context.SetColor(Muted);
            context.DrawString("(empty)", Padding, top, Anchor.Left | Anchor.Top);
            return;
        }

        int rowHeight = font.Height + 6;
        int visibleRows = Math.Max(1, available / rowHeight);

        // Scroll so the focused row stays on screen, keeping it centred where possible.
        int first = Math.Clamp(choice.Focus - (visibleRows / 2), 0,
            Math.Max(0, choice.Texts.Count - visibleRows));

        for (int row = 0; row < visibleRows && first + row < choice.Texts.Count; row++)
        {
            int index = first + row;
            int y = top + (row * rowHeight);
            bool focused = index == choice.Focus;

            if (focused)
            {
                context.SetColor(SelectionBackground);
                context.FillRect(0, y, width, rowHeight);
            }

            context.SetColor(focused ? SelectionForeground : Foreground);

            int textX = Padding;
            if (choice.ChoiceType is Choice.Multiple or Choice.Exclusive)
            {
                // A checkbox for multiple choice, a radio dot for exclusive.
                bool marked = choice.ChoiceType == Choice.Multiple
                    ? choice.Selected[index]
                    : choice.Focus == index;

                int boxSize = font.Height - 4;
                int boxY = y + 3;
                if (choice.ChoiceType == Choice.Multiple) context.DrawRect(Padding, boxY, boxSize, boxSize);
                else context.DrawArc(Padding, boxY, boxSize, boxSize, 0, 360);

                if (marked) context.FillRect(Padding + 3, boxY + 3, boxSize - 5, boxSize - 5);
                textX = Padding + boxSize + 5;
            }

            if (choice.Images[index] is { } icon)
            {
                context.DrawImage(icon.Surface, textX, y + (rowHeight / 2), Anchor.Left | Anchor.VCenter);
                textX += icon.Width + 4;
            }

            context.DrawString(choice.Texts[index], textX, y + 3, Anchor.Left | Anchor.Top);
        }

        DrawScrollHint(context, font, choice.Focus, choice.Texts.Count, visibleRows, top, available, width);
    }

    private static void DrawForm(
        JavaThread thread,
        GraphicsContext context,
        BitmapFont font,
        JavaForm form,
        int top,
        int available,
        int width)
    {
        int y = top;
        int lineHeight = font.Height + 3;

        foreach (JavaObject item in form.Items)
        {
            if (y > top + available) break;

            string? label = VirtualMachine.ToHostString(
                item.GetField(FormsModule.ItemLabelSlot).Reference as JavaObject);

            if (!string.IsNullOrEmpty(label))
            {
                context.SetColor(Muted);
                context.DrawString(label, Padding, y, Anchor.Left | Anchor.Top);
                y += lineHeight;
            }

            context.SetColor(Foreground);
            y = DrawItemBody(thread, context, font, item, y, width, lineHeight);
            y += 3;
        }
    }

    private static int DrawItemBody(
        JavaThread thread,
        GraphicsContext context,
        BitmapFont font,
        JavaObject item,
        int y,
        int width,
        int lineHeight)
    {
        switch (item.Class.Name)
        {
            case "javax/microedition/lcdui/StringItem":
            {
                string? text = VirtualMachine.ToHostString(item.GetField(1).Reference as JavaObject);
                foreach (string line in Wrap(text ?? string.Empty, font, width - (Padding * 2)))
                {
                    context.DrawString(line, Padding, y, Anchor.Left | Anchor.Top);
                    y += lineHeight;
                }
                return y;
            }

            case "javax/microedition/lcdui/ImageItem":
            {
                if (item.GetField(1).Reference is JavaImage image)
                {
                    context.DrawImage(image.Surface, Padding, y, Anchor.Left | Anchor.Top);
                    return y + image.Height;
                }
                return y;
            }

            case "javax/microedition/lcdui/Gauge":
            {
                int value = item.GetField(1).AsInt;
                int maximum = Math.Max(1, item.GetField(2).AsInt);
                int barWidth = width - (Padding * 2);
                int filled = Math.Clamp(value, 0, maximum) * barWidth / maximum;

                context.DrawRect(Padding, y, barWidth, font.Height);
                context.FillRect(Padding + 1, y + 1, Math.Max(0, filled - 2), font.Height - 1);
                return y + font.Height + 2;
            }

            case "javax/microedition/lcdui/TextField":
            {
                var entry = (JavaTextEntry)item;
                string shown = Mask(entry);
                context.DrawRect(Padding, y, width - (Padding * 2), font.Height + 2);
                context.DrawString(shown, Padding + 2, y + 1, Anchor.Left | Anchor.Top);
                return y + font.Height + 4;
            }

            case "javax/microedition/lcdui/ChoiceGroup":
            {
                var choice = (JavaChoice)item;
                for (int i = 0; i < choice.Texts.Count; i++)
                {
                    string marker = choice.Focus == i ? "> " : "  ";
                    context.DrawString(marker + choice.Texts[i], Padding, y, Anchor.Left | Anchor.Top);
                    y += lineHeight;
                }
                return y;
            }

            default:
            {
                // An item type this renderer does not know: fall back to its toString so that
                // something meaningful still appears rather than a gap.
                JavaValue text = thread.Vm.CallVirtual(thread, item, "toString", "()Ljava/lang/String;");
                context.DrawString(
                    VirtualMachine.ToHostString(text.Reference as JavaObject) ?? string.Empty,
                    Padding, y, Anchor.Left | Anchor.Top);
                return y + lineHeight;
            }
        }
    }

    private static void DrawTextEntry(
        GraphicsContext context,
        BitmapFont font,
        JavaTextEntry entry,
        int top,
        int available,
        int width)
    {
        context.SetColor(Foreground);

        string shown = Mask(entry);
        int y = top;
        foreach (string line in Wrap(shown, font, width - (Padding * 2)))
        {
            if (y > top + available - font.Height) break;
            context.DrawString(line, Padding, y, Anchor.Left | Anchor.Top);
            y += font.Height + 2;
        }

        // A caret, so it is obvious the field has focus and where typing will land.
        int caretX = Padding + font.StringWidth(shown.AsSpan(0, Math.Min(entry.Caret, shown.Length)));
        context.DrawLine(caretX, top, caretX, top + font.Height);

        context.SetColor(Muted);
        context.DrawString($"{entry.Text.Length}/{entry.MaxSize}",
            width - Padding, top + available - font.Height, Anchor.Right | Anchor.Top);
    }

    private static void DrawAlert(
        GraphicsContext context,
        BitmapFont font,
        JavaAlert alert,
        int top,
        int available,
        int width)
    {
        int y = top;

        if (alert.Image is { } image)
        {
            context.DrawImage(image.Surface, width / 2, y, Anchor.HCenter | Anchor.Top);
            y += image.Height + Padding;
        }

        context.SetColor(Foreground);
        foreach (string line in Wrap(alert.Text ?? string.Empty, font, width - (Padding * 2)))
        {
            if (y > top + available - font.Height) break;
            context.DrawString(line, width / 2, y, Anchor.HCenter | Anchor.Top);
            y += font.Height + 2;
        }
    }

    private static void DrawScrollHint(
        GraphicsContext context,
        BitmapFont font,
        int focus,
        int total,
        int visible,
        int top,
        int available,
        int width)
    {
        if (total <= visible) return;

        context.SetColor(Muted);
        context.DrawString($"{focus + 1}/{total}", width - 2, top + available - font.Height,
            Anchor.Right | Anchor.Top);
    }

    /// <summary>Replaces the text of a password field with bullets.</summary>
    private static string Mask(JavaTextEntry entry) =>
        (entry.Constraints & 0x10000) != 0
            ? new string('*', entry.Text.Length)
            : entry.Text.ToString();

    /// <summary>Greedy word wrap against the measured width of the font.</summary>
    private static List<string> Wrap(string text, BitmapFont font, int width)
    {
        var lines = new List<string>();
        if (text.Length == 0 || width <= 0) return lines;

        foreach (string paragraph in text.Split('\n'))
        {
            string current = string.Empty;
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (font.StringWidth(candidate) <= width)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0) lines.Add(current);
                current = word;
            }

            lines.Add(current);
        }

        return lines;
    }

    // ---------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------

    /// <summary>Drives the focused widget from a key press.</summary>
    public static void HandleKey(JavaThread thread, MidpRuntime runtime, JavaObject displayable, int keyCode)
    {
        int action = runtime.Device.GetGameAction(keyCode);

        switch (displayable)
        {
            case JavaChoice choice:
                HandleChoiceKey(thread, runtime, choice, action, keyCode);
                break;

            case JavaForm form when form.Items.Count > 0:
                if (action == GameAction.Up) form.Focus = Math.Max(0, form.Focus - 1);
                else if (action == GameAction.Down) form.Focus = Math.Min(form.Items.Count - 1, form.Focus + 1);
                Repaint(runtime);
                break;

            case JavaTextEntry entry:
                HandleTextKey(runtime, entry, keyCode);
                break;

            case JavaAlert alert when action == GameAction.Fire:
                // Dismissing an alert shows whatever was queued behind it.
                if (alert.Next is { } next) LcduiModule.SetCurrent(thread, next);
                break;
        }
    }

    private static void HandleChoiceKey(
        JavaThread thread,
        MidpRuntime runtime,
        JavaChoice choice,
        int action,
        int keyCode)
    {
        if (choice.Texts.Count == 0) return;

        switch (action)
        {
            case GameAction.Up:
                choice.Focus = choice.Focus == 0 ? choice.Texts.Count - 1 : choice.Focus - 1;
                Repaint(runtime);
                return;

            case GameAction.Down:
                choice.Focus = choice.Focus == choice.Texts.Count - 1 ? 0 : choice.Focus + 1;
                Repaint(runtime);
                return;

            case GameAction.Fire:
                if (choice.ChoiceType == Choice.Multiple)
                {
                    choice.Selected[choice.Focus] = !choice.Selected[choice.Focus];
                    Repaint(runtime);
                    return;
                }

                // An implicit list reports the selection through SELECT_COMMAND, which is what
                // menu code listens for.
                if (choice.ChoiceType == Choice.Implicit)
                {
                    JavaClass list = thread.Vm.LoadClass("javax/microedition/lcdui/List");
                    thread.Vm.EnsureInitialized(thread, list);
                    if (list.StaticFields[0].Reference is JavaObject select)
                        MidpDispatcher.DeliverCommand(thread, choice, select);
                }
                return;

            default:
                if (keyCode is >= KeyCodes.Num1 and <= KeyCodes.Num9)
                {
                    // Numeric shortcut selection, which handsets supported and menus relied on.
                    int index = keyCode - KeyCodes.Num1;
                    if (index < choice.Texts.Count)
                    {
                        choice.Focus = index;
                        Repaint(runtime);
                    }
                }
                return;
        }
    }

    private static void HandleTextKey(MidpRuntime runtime, JavaTextEntry entry, int keyCode)
    {
        switch (keyCode)
        {
            case KeyCodes.Clear when entry.Caret > 0:
                entry.Text.Remove(entry.Caret - 1, 1);
                entry.Caret--;
                break;

            case KeyCodes.Left:
                entry.Caret = Math.Max(0, entry.Caret - 1);
                break;

            case KeyCodes.Right:
                entry.Caret = Math.Min(entry.Text.Length, entry.Caret + 1);
                break;

            default:
                if (keyCode is >= 32 and < 127 && entry.Text.Length < entry.MaxSize)
                {
                    entry.Text.Insert(entry.Caret, (char)keyCode);
                    entry.Caret++;
                }
                break;
        }

        Repaint(runtime);
    }

    private static void Repaint(MidpRuntime runtime) =>
        runtime.Events.PostRepaint(0, 0, runtime.CanvasWidth, runtime.CanvasHeight);
}
