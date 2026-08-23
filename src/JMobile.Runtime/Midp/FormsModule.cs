using System.Text;
using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;

namespace JMobile.Runtime.Midp;

/// <summary>A <c>List</c> or <c>ChoiceGroup</c> and the entries it holds.</summary>
public sealed class JavaChoice(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public List<string> Texts { get; } = [];

    public List<JavaImage?> Images { get; } = [];

    public List<bool> Selected { get; } = [];

    /// <summary>One of the <c>Choice</c> type constants.</summary>
    public int ChoiceType { get; set; } = Choice.Implicit;

    public int Focus { get; set; }

    public void Insert(int index, string text, JavaImage? image)
    {
        Texts.Insert(index, text);
        Images.Insert(index, image);
        Selected.Insert(index, false);
    }

    public void RemoveAt(int index)
    {
        Texts.RemoveAt(index);
        Images.RemoveAt(index);
        Selected.RemoveAt(index);
        if (Focus >= Texts.Count) Focus = Math.Max(0, Texts.Count - 1);
    }

    public void Clear()
    {
        Texts.Clear();
        Images.Clear();
        Selected.Clear();
        Focus = 0;
    }
}

/// <summary>Type constants from the <c>Choice</c> interface.</summary>
public static class Choice
{
    public const int Exclusive = 1;
    public const int Multiple = 2;
    public const int Implicit = 3;
    public const int Popup = 4;
}

/// <summary>A <c>Form</c> and the items laid out on it.</summary>
public sealed class JavaForm(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public List<JavaObject> Items { get; } = [];

    public int Focus { get; set; }
}

/// <summary>A <c>TextBox</c> or the editing state of a <c>TextField</c>.</summary>
public sealed class JavaTextEntry(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public StringBuilder Text { get; } = new();

    public int MaxSize { get; set; } = 128;

    public int Constraints { get; set; }

    public int Caret { get; set; }
}

/// <summary>An <c>Alert</c> and what follows when it is dismissed.</summary>
public sealed class JavaAlert(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public string? Text { get; set; }

    public JavaImage? Image { get; set; }

    public int AlertType { get; set; }

    /// <summary>Milliseconds before the alert dismisses itself, or -2 to mean "forever".</summary>
    public int Timeout { get; set; } = 2000;

    public JavaObject? Next { get; set; }
}

/// <summary>
/// The high-level screens: Form, List, TextBox, Alert and the Item hierarchy.
/// </summary>
/// <remarks>
/// These are the parts of MIDP a real handset drew itself, and so does this runtime; there is
/// no <c>paint</c> method for a game to override. Most action games touch none of it, but menus,
/// high-score entry and "are you sure?" prompts are frequently built from these classes, so a
/// game that uses them has to look and behave sensibly rather than showing a blank screen.
/// </remarks>
public sealed class FormsModule : IRuntimeModule
{
    public string Name => "javax.microedition.lcdui forms";

    /// <summary>Item field slot for the label.</summary>
    internal const int ItemLabelSlot = 0;

    public void Register(RuntimeRegistry registry)
    {
        RegisterChoiceInterface(registry);
        RegisterItems(registry);
        RegisterForm(registry);
        RegisterList(registry);
        RegisterTextBox(registry);
        RegisterAlert(registry);
    }

    /// <summary>Records what should be shown after an alert is dismissed.</summary>
    public static void SetAlertNext(JavaObject alert, JavaObject? next)
    {
        if (alert is JavaAlert typed) typed.Next = next;
    }

    private static void RegisterChoiceInterface(RuntimeRegistry registry)
    {
        registry.DefineInterface("javax/microedition/lcdui/Choice", builder => builder
            .Constant("EXCLUSIVE", Choice.Exclusive)
            .Constant("MULTIPLE", Choice.Multiple)
            .Constant("IMPLICIT", Choice.Implicit)
            .Constant("POPUP", Choice.Popup)
            .Constant("TEXT_WRAP_DEFAULT", 0)
            .Constant("TEXT_WRAP_ON", 1)
            .Constant("TEXT_WRAP_OFF", 2));
    }

    // ---------------------------------------------------------------------------------
    // Items
    // ---------------------------------------------------------------------------------

    private static void RegisterItems(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Item", builder => builder
            .AsAbstract()
            .Field("label", "Ljava/lang/String;")
            .Constant("PLAIN", 0)
            .Constant("HYPERLINK", 1)
            .Constant("BUTTON", 2)
            .Constant("LAYOUT_DEFAULT", 0)
            .Constant("LAYOUT_LEFT", 1)
            .Constant("LAYOUT_RIGHT", 2)
            .Constant("LAYOUT_CENTER", 3)
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("getLabel", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(ItemLabelSlot).Reference))
            .Method("setLabel", "(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .NoOpMethod("setLayout", "(I)V")
            .NoOpMethod("addCommand", "(Ljavax/microedition/lcdui/Command;)V")
            .NoOpMethod("setItemCommandListener", "(Ljavax/microedition/lcdui/ItemCommandListener;)V"));

        registry.Define("javax/microedition/lcdui/StringItem", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .Field("text", "Ljava/lang/String;")
            .Constructor("(Ljava/lang/String;Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                args.This.SetField(1, JavaValue.Ref(args.Object(1)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;Ljava/lang/String;I)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                args.This.SetField(1, JavaValue.Ref(args.Object(1)));
                return JavaValue.Void;
            })
            .Method("getText", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(1).Reference))
            .Method("setText", "(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            }));

        registry.Define("javax/microedition/lcdui/ImageItem", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .Field("image", "Ljavax/microedition/lcdui/Image;")
            .Field("altText", "Ljava/lang/String;")
            .Constructor("(Ljava/lang/String;Ljavax/microedition/lcdui/Image;ILjava/lang/String;)V",
                static (_, args) =>
                {
                    args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                    args.This.SetField(1, JavaValue.Ref(args.Object(1)));
                    args.This.SetField(2, JavaValue.Ref(args.Object(3)));
                    return JavaValue.Void;
                })
            .Method("getImage", "()Ljavax/microedition/lcdui/Image;",
                static (_, args) => JavaValue.Ref(args.This.GetField(1).Reference))
            .Method("setImage", "(Ljavax/microedition/lcdui/Image;)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            }));

        registry.Define("javax/microedition/lcdui/Gauge", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .Field("value", "I")
            .Field("maxValue", "I")
            .Field("interactive", "Z")
            .Constant("INDEFINITE", -1)
            .Constant("CONTINUOUS_IDLE", 0)
            .Constant("INCREMENTAL_IDLE", 1)
            .Constant("CONTINUOUS_RUNNING", 2)
            .Constant("INCREMENTAL_UPDATING", 3)
            .Constructor("(Ljava/lang/String;ZII)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                args.This.SetField(3, JavaValue.Bool(args.Bool(1)));
                args.This.SetField(2, JavaValue.Int(args.Int(2)));
                args.This.SetField(1, JavaValue.Int(args.Int(3)));
                return JavaValue.Void;
            })
            .Method("getValue", "()I", static (_, args) => args.This.GetField(1))
            .Method("setValue", "(I)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Int(args.Int(0)));
                return JavaValue.Void;
            })
            .Method("getMaxValue", "()I", static (_, args) => args.This.GetField(2))
            .Method("setMaxValue", "(I)V", static (_, args) =>
            {
                args.This.SetField(2, JavaValue.Int(args.Int(0)));
                return JavaValue.Void;
            })
            .Method("isInteractive", "()Z", static (_, args) => args.This.GetField(3)));

        // Spacer and DateField complete the MIDP 2.0 Item hierarchy. Neither is common in games,
        // but a Form that references one fails at class loading before drawing anything, so the
        // cost of not having them is total rather than cosmetic.
        registry.Define("javax/microedition/lcdui/Spacer", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .Field("minWidth", "I")
            .Field("minHeight", "I")
            .Constructor("(II)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Int(args.Int(0)));
                args.This.SetField(2, JavaValue.Int(args.Int(1)));
                return JavaValue.Void;
            })
            .Method("setMinimumSize", "(II)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Int(args.Int(0)));
                args.This.SetField(2, JavaValue.Int(args.Int(1)));
                return JavaValue.Void;
            }));

        registry.Define("javax/microedition/lcdui/DateField", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .Field("date", "Ljava/util/Date;")
            .Field("mode", "I")
            .Constant("DATE", 1)
            .Constant("TIME", 2)
            .Constant("DATE_TIME", 3)
            .Constructor("(Ljava/lang/String;I)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                args.This.SetField(2, JavaValue.Int(args.Int(1)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;ILjava/util/TimeZone;)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                args.This.SetField(2, JavaValue.Int(args.Int(1)));
                return JavaValue.Void;
            })
            .Method("getDate", "()Ljava/util/Date;",
                static (_, args) => JavaValue.Ref(args.This.GetField(1).Reference))
            .Method("setDate", "(Ljava/util/Date;)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("getInputMode", "()I", static (_, args) => args.This.GetField(2))
            .Method("setInputMode", "(I)V", static (_, args) =>
            {
                args.This.SetField(2, JavaValue.Int(args.Int(0)));
                return JavaValue.Void;
            }));

        registry.Define("javax/microedition/lcdui/TextField", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .AllocatesWith(static type => new JavaTextEntry(type, type.InstanceFieldCount))
            .Constant("ANY", 0)
            .Constant("EMAILADDR", 1)
            .Constant("NUMERIC", 2)
            .Constant("PHONENUMBER", 3)
            .Constant("URL", 4)
            .Constant("DECIMAL", 5)
            .Constant("PASSWORD", 0x10000)
            .Constant("UNEDITABLE", 0x20000)
            .Constant("CONSTRAINT_MASK", 0xFFFF)
            .Constructor("(Ljava/lang/String;Ljava/lang/String;II)V", static (_, args) =>
            {
                var self = args.Self<JavaTextEntry>();
                self.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                self.Text.Append(args.String(1) ?? string.Empty);
                self.MaxSize = Math.Max(1, args.Int(2));
                self.Constraints = args.Int(3);
                self.Caret = self.Text.Length;
                return JavaValue.Void;
            })
            .Method("getString", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaTextEntry>().Text.ToString()))
            .Method("setString", "(Ljava/lang/String;)V", static (_, args) =>
            {
                var self = args.Self<JavaTextEntry>();
                self.Text.Clear().Append(args.String(0) ?? string.Empty);
                self.Caret = self.Text.Length;
                return JavaValue.Void;
            })
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTextEntry>().Text.Length))
            .Method("getMaxSize", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTextEntry>().MaxSize))
            .Method("setMaxSize", "(I)I", static (_, args) =>
            {
                args.Self<JavaTextEntry>().MaxSize = Math.Max(1, args.Int(0));
                return JavaValue.Int(args.Self<JavaTextEntry>().MaxSize);
            })
            .Method("getConstraints", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaTextEntry>().Constraints))
            .Method("setConstraints", "(I)V", static (_, args) =>
            {
                args.Self<JavaTextEntry>().Constraints = args.Int(0);
                return JavaValue.Void;
            }));
    }

    // ---------------------------------------------------------------------------------
    // Form
    // ---------------------------------------------------------------------------------

    private static void RegisterForm(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Form", builder => builder
            .Extends("javax/microedition/lcdui/Screen")
            .AllocatesWith(static type => new JavaForm(type, type.InstanceFieldCount))
            .Constructor("(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;[Ljavax/microedition/lcdui/Item;)V", static (_, args) =>
            {
                args.This.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                if (args.Object<JavaArray<JavaObject?>>(1) is { } items)
                {
                    foreach (JavaObject? item in items.Items)
                    {
                        if (item is not null) args.Self<JavaForm>().Items.Add(item);
                    }
                }
                return JavaValue.Void;
            })
            .Method("append", "(Ljavax/microedition/lcdui/Item;)I", static (thread, args) =>
            {
                JavaObject item = RuntimeSupport.RequireRef(thread, args.Object(0), "item");
                var self = args.Self<JavaForm>();
                self.Items.Add(item);
                Refresh(thread, self);
                return JavaValue.Int(self.Items.Count - 1);
            })
            .Method("append", "(Ljava/lang/String;)I", static (thread, args) =>
            {
                JavaObject item = thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("javax/microedition/lcdui/StringItem"),
                    "(Ljava/lang/String;Ljava/lang/String;)V",
                    JavaValue.Null, JavaValue.Ref(args.Object(0)));

                var self = args.Self<JavaForm>();
                self.Items.Add(item);
                Refresh(thread, self);
                return JavaValue.Int(self.Items.Count - 1);
            })
            .Method("append", "(Ljavax/microedition/lcdui/Image;)I", static (thread, args) =>
            {
                JavaObject item = thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("javax/microedition/lcdui/ImageItem"),
                    "(Ljava/lang/String;Ljavax/microedition/lcdui/Image;ILjava/lang/String;)V",
                    JavaValue.Null, JavaValue.Ref(args.Object(0)), JavaValue.Int(0), JavaValue.Null);

                var self = args.Self<JavaForm>();
                self.Items.Add(item);
                Refresh(thread, self);
                return JavaValue.Int(self.Items.Count - 1);
            })
            .Method("insert", "(ILjavax/microedition/lcdui/Item;)V", static (thread, args) =>
            {
                var self = args.Self<JavaForm>();
                int index = Math.Clamp(args.Int(0), 0, self.Items.Count);
                self.Items.Insert(index, RuntimeSupport.RequireRef(thread, args.Object(1), "item"));
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("delete", "(I)V", static (thread, args) =>
            {
                var self = args.Self<JavaForm>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Items.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());

                self.Items.RemoveAt(index);
                if (self.Focus >= self.Items.Count) self.Focus = Math.Max(0, self.Items.Count - 1);
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("deleteAll", "()V", static (thread, args) =>
            {
                var self = args.Self<JavaForm>();
                self.Items.Clear();
                self.Focus = 0;
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("set", "(ILjavax/microedition/lcdui/Item;)V", static (thread, args) =>
            {
                var self = args.Self<JavaForm>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Items.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());

                self.Items[index] = RuntimeSupport.RequireRef(thread, args.Object(1), "item");
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("get", "(I)Ljavax/microedition/lcdui/Item;", static (thread, args) =>
            {
                var self = args.Self<JavaForm>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Items.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());
                return JavaValue.Ref(self.Items[index]);
            })
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaForm>().Items.Count))
            .NoOpMethod("setItemStateListener", "(Ljavax/microedition/lcdui/ItemStateListener;)V"));

        registry.DefineInterface("javax/microedition/lcdui/ItemStateListener", builder =>
            builder.InterfaceMethod("itemStateChanged", "(Ljavax/microedition/lcdui/Item;)V"));
    }

    // ---------------------------------------------------------------------------------
    // List
    // ---------------------------------------------------------------------------------

    private static void RegisterList(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/List", builder => builder
            .Extends("javax/microedition/lcdui/Screen")
            .Implements("javax/microedition/lcdui/Choice")
            .AllocatesWith(static type => new JavaChoice(type, type.InstanceFieldCount))
            .StaticField("SELECT_COMMAND", "Ljavax/microedition/lcdui/Command;")
            .StaticInitializer(static (thread, _) =>
            {
                // SELECT_COMMAND is a shared singleton games compare by reference in
                // commandAction, so it has to exist before any list does.
                JavaClass list = thread.Vm.LoadClass("javax/microedition/lcdui/List");
                list.StaticFields[0] = JavaValue.Ref(thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("javax/microedition/lcdui/Command"),
                    "(Ljava/lang/String;II)V",
                    JavaValue.Ref(thread.Vm.NewString("Select")),
                    JavaValue.Int(4),   // Command.OK
                    JavaValue.Int(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;I)V", static (_, args) =>
            {
                args.This.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                args.Self<JavaChoice>().ChoiceType = args.Int(1);
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;I[Ljava/lang/String;[Ljavax/microedition/lcdui/Image;)V",
                static (_, args) =>
                {
                    args.This.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                    var self = args.Self<JavaChoice>();
                    self.ChoiceType = args.Int(1);

                    if (args.Object<JavaArray<JavaObject?>>(2) is { } texts)
                    {
                        var images = args.Object<JavaArray<JavaObject?>>(3);
                        for (int i = 0; i < texts.Length; i++)
                        {
                            self.Insert(self.Texts.Count,
                                (texts.Items[i] as JavaString)?.Value ?? string.Empty,
                                images is not null && i < images.Length ? images.Items[i] as JavaImage : null);
                        }
                    }
                    return JavaValue.Void;
                })
            .Method("append", "(Ljava/lang/String;Ljavax/microedition/lcdui/Image;)I", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                self.Insert(self.Texts.Count, args.String(0) ?? string.Empty, args.Object<JavaImage>(1));
                Refresh(thread, self);
                return JavaValue.Int(self.Texts.Count - 1);
            })
            .Method("insert", "(ILjava/lang/String;Ljavax/microedition/lcdui/Image;)V", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = Math.Clamp(args.Int(0), 0, self.Texts.Count);
                self.Insert(index, args.String(1) ?? string.Empty, args.Object<JavaImage>(2));
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("set", "(ILjava/lang/String;Ljavax/microedition/lcdui/Image;)V", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Texts.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());

                self.Texts[index] = args.String(1) ?? string.Empty;
                self.Images[index] = args.Object<JavaImage>(2);
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("delete", "(I)V", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Texts.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());

                self.RemoveAt(index);
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("deleteAll", "()V", static (thread, args) =>
            {
                args.Self<JavaChoice>().Clear();
                Refresh(thread, args.This);
                return JavaValue.Void;
            })
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaChoice>().Texts.Count))
            .Method("getString", "(I)Ljava/lang/String;", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Texts.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());
                return RuntimeSupport.Str(thread, self.Texts[index]);
            })
            .Method("getImage", "(I)Ljavax/microedition/lcdui/Image;", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Images.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());
                return JavaValue.Ref(self.Images[index]);
            })
            .Method("getSelectedIndex", "()I", static (_, args) =>
            {
                var self = args.Self<JavaChoice>();
                // A multiple-choice list has no single selection, which is what -1 conveys.
                return JavaValue.Int(self.ChoiceType == Choice.Multiple ? -1 : self.Focus);
            })
            .Method("setSelectedIndex", "(IZ)V", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Texts.Count) return JavaValue.Void;

                if (self.ChoiceType == Choice.Multiple) self.Selected[index] = args.Bool(1);
                else if (args.Bool(1)) self.Focus = index;

                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("isSelected", "(I)Z", static (_, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Texts.Count) return JavaValue.Bool(false);
                return JavaValue.Bool(self.ChoiceType == Choice.Multiple
                    ? self.Selected[index]
                    : self.Focus == index);
            })
            .Method("getSelectedFlags", "([Z)I", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                var flags = args.Object<JavaArray<sbyte>>(0)
                            ?? throw thread.Vm.NewNullPointerException(thread, "selectedArray");

                int count = 0;
                for (int i = 0; i < self.Texts.Count && i < flags.Length; i++)
                {
                    bool selected = self.ChoiceType == Choice.Multiple ? self.Selected[i] : self.Focus == i;
                    flags.Items[i] = (sbyte)(selected ? 1 : 0);
                    if (selected) count++;
                }
                return JavaValue.Int(count);
            })
            .NoOpMethod("setFitPolicy", "(I)V")
            .NoOpMethod("setTicker", "(Ljavax/microedition/lcdui/Ticker;)V"));

        registry.Define("javax/microedition/lcdui/ChoiceGroup", builder => builder
            .Extends("javax/microedition/lcdui/Item")
            .Implements("javax/microedition/lcdui/Choice")
            .AllocatesWith(static type => new JavaChoice(type, type.InstanceFieldCount))
            .Constructor("(Ljava/lang/String;I)V", static (_, args) =>
            {
                args.This.SetField(ItemLabelSlot, JavaValue.Ref(args.Object(0)));
                args.Self<JavaChoice>().ChoiceType = args.Int(1);
                return JavaValue.Void;
            })
            .Method("append", "(Ljava/lang/String;Ljavax/microedition/lcdui/Image;)I", static (_, args) =>
            {
                var self = args.Self<JavaChoice>();
                self.Insert(self.Texts.Count, args.String(0) ?? string.Empty, args.Object<JavaImage>(1));
                return JavaValue.Int(self.Texts.Count - 1);
            })
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaChoice>().Texts.Count))
            .Method("getSelectedIndex", "()I", static (_, args) => JavaValue.Int(args.Self<JavaChoice>().Focus))
            .Method("setSelectedIndex", "(IZ)V", static (_, args) =>
            {
                var self = args.Self<JavaChoice>();
                if (args.Bool(1) && (uint)args.Int(0) < (uint)self.Texts.Count) self.Focus = args.Int(0);
                return JavaValue.Void;
            })
            .Method("getString", "(I)Ljava/lang/String;", static (thread, args) =>
            {
                var self = args.Self<JavaChoice>();
                int index = args.Int(0);
                return (uint)index < (uint)self.Texts.Count
                    ? RuntimeSupport.Str(thread, self.Texts[index])
                    : JavaValue.Null;
            }));
    }

    // ---------------------------------------------------------------------------------
    // TextBox
    // ---------------------------------------------------------------------------------

    private static void RegisterTextBox(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/TextBox", builder => builder
            .Extends("javax/microedition/lcdui/Screen")
            .AllocatesWith(static type => new JavaTextEntry(type, type.InstanceFieldCount))
            .Constructor("(Ljava/lang/String;Ljava/lang/String;II)V", static (_, args) =>
            {
                var self = args.Self<JavaTextEntry>();
                self.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                self.Text.Append(args.String(1) ?? string.Empty);
                self.MaxSize = Math.Max(1, args.Int(2));
                self.Constraints = args.Int(3);
                self.Caret = self.Text.Length;
                return JavaValue.Void;
            })
            .Method("getString", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaTextEntry>().Text.ToString()))
            .Method("setString", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                var self = args.Self<JavaTextEntry>();
                self.Text.Clear().Append(args.String(0) ?? string.Empty);
                self.Caret = self.Text.Length;
                Refresh(thread, self);
                return JavaValue.Void;
            })
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTextEntry>().Text.Length))
            .Method("getMaxSize", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTextEntry>().MaxSize))
            .Method("setMaxSize", "(I)I", static (_, args) =>
            {
                var self = args.Self<JavaTextEntry>();
                self.MaxSize = Math.Max(1, args.Int(0));
                return JavaValue.Int(self.MaxSize);
            })
            .Method("getConstraints", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaTextEntry>().Constraints))
            .Method("setConstraints", "(I)V", static (_, args) =>
            {
                args.Self<JavaTextEntry>().Constraints = args.Int(0);
                return JavaValue.Void;
            }));
    }

    // ---------------------------------------------------------------------------------
    // Alert
    // ---------------------------------------------------------------------------------

    private static void RegisterAlert(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/AlertType", builder => builder
            .StaticField("INFO", "Ljavax/microedition/lcdui/AlertType;")
            .StaticField("WARNING", "Ljavax/microedition/lcdui/AlertType;")
            .StaticField("ERROR", "Ljavax/microedition/lcdui/AlertType;")
            .StaticField("ALARM", "Ljavax/microedition/lcdui/AlertType;")
            .StaticField("CONFIRMATION", "Ljavax/microedition/lcdui/AlertType;")
            .Field("kind", "I")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .StaticInitializer(static (thread, _) =>
            {
                JavaClass type = thread.Vm.LoadClass("javax/microedition/lcdui/AlertType");
                for (int kind = 0; kind < 5; kind++)
                {
                    JavaObject instance = thread.Vm.Construct(thread, type, "()V");
                    instance.SetField(0, JavaValue.Int(kind));
                    type.StaticFields[kind] = JavaValue.Ref(instance);
                }
                return JavaValue.Void;
            })
            .Method("playSound", "(Ljavax/microedition/lcdui/Display;)Z", static (thread, args) =>
            {
                // The alert tones were distinct on real handsets; a short beep is close enough
                // to convey that something happened without being obnoxious.
                MidpRuntime.Of(thread.Vm).Audio?.PlayTone(72, 120, 40);
                return JavaValue.Bool(true);
            }));

        registry.Define("javax/microedition/lcdui/Alert", builder => builder
            .Extends("javax/microedition/lcdui/Screen")
            .AllocatesWith(static type => new JavaAlert(type, type.InstanceFieldCount))
            .Constant("FOREVER", -2)
            .Constructor("(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Constructor(
                "(Ljava/lang/String;Ljava/lang/String;Ljavax/microedition/lcdui/Image;Ljavax/microedition/lcdui/AlertType;)V",
                static (_, args) =>
                {
                    var self = args.Self<JavaAlert>();
                    self.SetField(LcduiModule.TitleSlot, JavaValue.Ref(args.Object(0)));
                    self.Text = args.String(1);
                    self.Image = args.Object<JavaImage>(2);
                    self.AlertType = args.Object(3)?.GetField(0).AsInt ?? 0;
                    return JavaValue.Void;
                })
            .Method("setString", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                args.Self<JavaAlert>().Text = args.String(0);
                Refresh(thread, args.This);
                return JavaValue.Void;
            })
            .Method("getString", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaAlert>().Text))
            .Method("setImage", "(Ljavax/microedition/lcdui/Image;)V", static (thread, args) =>
            {
                args.Self<JavaAlert>().Image = args.Object<JavaImage>(0);
                Refresh(thread, args.This);
                return JavaValue.Void;
            })
            .Method("setTimeout", "(I)V", static (_, args) =>
            {
                args.Self<JavaAlert>().Timeout = args.Int(0);
                return JavaValue.Void;
            })
            .Method("getTimeout", "()I", static (_, args) => JavaValue.Int(args.Self<JavaAlert>().Timeout))
            .Method("getDefaultTimeout", "()I", static (_, _) => JavaValue.Int(2000))
            .NoOpMethod("setType", "(Ljavax/microedition/lcdui/AlertType;)V")
            .NoOpMethod("setIndicator", "(Ljavax/microedition/lcdui/Gauge;)V"));
    }

    /// <summary>Requests a repaint after a widget's contents change.</summary>
    private static void Refresh(JavaThread thread, JavaObject displayable)
    {
        MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
        if (ReferenceEquals(runtime.CurrentDisplayable, displayable))
            runtime.Events.PostRepaint(0, 0, runtime.CanvasWidth, runtime.CanvasHeight);
    }
}
