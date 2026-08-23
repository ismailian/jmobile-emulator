using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Midp;

namespace JMobile.Runtime.Media;

/// <summary>
/// The Mobile Media API subset MIDP 2.0 requires: tones and sampled playback.
/// </summary>
/// <remarks>
/// Games treat audio as fire-and-forget, and a game whose sound fails should keep playing rather
/// than crash. Every entry point here therefore degrades to silence when no audio host is
/// available or the format is unsupported, and only reports a <c>MediaException</c> where the
/// specification requires the game to be told.
/// </remarks>
public sealed class MediaModule : IRuntimeModule
{
    public string Name => "javax.microedition.media";

    /// <summary>Player lifecycle states as defined by the MMAPI.</summary>
    private const int Unrealized = 100;
    private const int Realized = 200;
    private const int Prefetched = 300;
    private const int Started = 400;
    private const int Closed = 0;

    public void Register(RuntimeRegistry registry)
    {
        registry.DefineThrowable("javax/microedition/media/MediaException", WellKnown.Exception);

        RegisterManager(registry);
        RegisterPlayer(registry);
        RegisterControls(registry);
    }

    private static void RegisterManager(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/media/Manager", builder => builder
            .StaticField("TONE_DEVICE_LOCATOR", "Ljava/lang/String;")
            .StaticInitializer(static (thread, _) =>
            {
                JavaClass type = thread.Vm.LoadClass("javax/microedition/media/Manager");
                type.StaticFields[0] = JavaValue.Ref(thread.Vm.NewString("device://tone"));
                return JavaValue.Void;
            })
            .StaticMethod("playTone", "(III)V", static (thread, args) =>
            {
                MidpRuntime.Of(thread.Vm).Audio?.PlayTone(args.Int(0), args.Int(1), args.Int(2));
                return JavaValue.Void;
            })
            .StaticMethod("getSupportedContentTypes", "(Ljava/lang/String;)[Ljava/lang/String;",
                static (thread, _) => JavaValue.Ref(thread.Vm.NewStringArray(
                    ["audio/x-wav", "audio/wav", "audio/x-tone-seq", "audio/midi"])))
            .StaticMethod("getSupportedProtocols", "(Ljava/lang/String;)[Ljava/lang/String;",
                static (thread, _) => JavaValue.Ref(thread.Vm.NewStringArray(["device", "http", "resource"])))
            .StaticMethod("createPlayer",
                "(Ljava/io/InputStream;Ljava/lang/String;)Ljavax/microedition/media/Player;",
                static (thread, args) =>
                {
                    JavaObject stream = RuntimeSupport.RequireRef(thread, args.Object(0), "stream");
                    byte[] data = DrainStream(thread, stream);
                    return JavaValue.Ref(NewPlayer(thread, data, args.String(1) ?? "audio/x-wav"));
                })
            .StaticMethod("createPlayer", "(Ljava/lang/String;)Ljavax/microedition/media/Player;",
                static (thread, args) =>
                {
                    string locator = RuntimeSupport.RequireString(thread, args.String(0), "locator");

                    if (locator.StartsWith("device://", StringComparison.OrdinalIgnoreCase))
                        return JavaValue.Ref(NewPlayer(thread, [], "audio/x-tone-seq"));

                    if (locator.StartsWith("resource:", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = locator["resource:".Length..];
                        byte[] resource = thread.Vm.GetResource(path) ?? [];
                        return JavaValue.Ref(NewPlayer(thread, resource, GuessType(path)));
                    }

                    throw thread.Vm.NewException(thread, "javax/microedition/media/MediaException",
                        "unsupported locator: " + locator);
                }));
    }

    private static string GuessType(string path) =>
        path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase)
            ? "audio/midi"
            : "audio/x-wav";

    /// <summary>Reads a Java stream to the end, so the clip owns its own copy of the data.</summary>
    private static byte[] DrainStream(JavaThread thread, JavaObject stream)
    {
        using var buffer = new MemoryStream();
        while (true)
        {
            int value = thread.Vm.CallVirtual(thread, stream, "read", "()I").AsInt;
            if (value < 0) break;
            buffer.WriteByte((byte)value);
        }
        return buffer.ToArray();
    }

    private static JavaObject NewPlayer(JavaThread thread, byte[] data, string contentType)
    {
        JavaClass type = thread.Vm.LoadClass("javax/microedition/media/PlayerImpl");
        var player = (JavaPlayer)thread.Vm.NewInstance(type);
        player.ContentType = contentType;
        player.State = Unrealized;

        // The clip is prepared eagerly: a game that calls start() in a keypress handler must not
        // stall the event thread decoding audio.
        player.Clip = MidpRuntime.Of(thread.Vm).Audio?.CreateClip(data, contentType);
        return player;
    }

    private static void RegisterPlayer(RuntimeRegistry registry)
    {
        registry.DefineInterface("javax/microedition/media/Controllable", builder => builder
            .InterfaceMethod("getControl", "(Ljava/lang/String;)Ljavax/microedition/media/Control;")
            .InterfaceMethod("getControls", "()[Ljavax/microedition/media/Control;"));

        registry.DefineInterface("javax/microedition/media/Player", builder => builder
            .Constant("UNREALIZED", Unrealized)
            .Constant("REALIZED", Realized)
            .Constant("PREFETCHED", Prefetched)
            .Constant("STARTED", Started)
            .Constant("CLOSED", Closed)
            .Constant("TIME_UNKNOWN", -1L)
            .InterfaceMethod("realize", "()V")
            .InterfaceMethod("prefetch", "()V")
            .InterfaceMethod("start", "()V")
            .InterfaceMethod("stop", "()V")
            .InterfaceMethod("close", "()V")
            .InterfaceMethod("deallocate", "()V")
            .InterfaceMethod("getState", "()I")
            .InterfaceMethod("setLoopCount", "(I)V")
            .InterfaceMethod("getDuration", "()J")
            .InterfaceMethod("getMediaTime", "()J")
            .InterfaceMethod("setMediaTime", "(J)J")
            .InterfaceMethod("getContentType", "()Ljava/lang/String;")
            .InterfaceMethod("addPlayerListener", "(Ljavax/microedition/media/PlayerListener;)V")
            .InterfaceMethod("removePlayerListener", "(Ljavax/microedition/media/PlayerListener;)V"));

        registry.DefineInterface("javax/microedition/media/PlayerListener", builder => builder
            .InterfaceMethod("playerUpdate",
                "(Ljavax/microedition/media/Player;Ljava/lang/String;Ljava/lang/Object;)V"));

        registry.Define("javax/microedition/media/PlayerImpl", builder => builder
            .Implements("javax/microedition/media/Player", "javax/microedition/media/Controllable")
            .AllocatesWith(static type => new JavaPlayer(type, type.InstanceFieldCount))
            .Method("realize", "()V", static (_, args) =>
            {
                var self = args.Self<JavaPlayer>();
                if (self.State < Realized) self.State = Realized;
                return JavaValue.Void;
            })
            .Method("prefetch", "()V", static (_, args) =>
            {
                var self = args.Self<JavaPlayer>();
                if (self.State < Prefetched) self.State = Prefetched;
                return JavaValue.Void;
            })
            .Method("start", "()V", static (_, args) =>
            {
                var self = args.Self<JavaPlayer>();
                if (self.State == Closed) return JavaValue.Void;

                self.State = Started;
                self.Clip?.Play(self.LoopCount);
                return JavaValue.Void;
            })
            .Method("stop", "()V", static (_, args) =>
            {
                var self = args.Self<JavaPlayer>();
                self.Clip?.Stop();
                if (self.State == Started) self.State = Prefetched;
                return JavaValue.Void;
            })
            .Method("close", "()V", static (_, args) =>
            {
                var self = args.Self<JavaPlayer>();
                self.Clip?.Dispose();
                self.Clip = null;
                self.State = Closed;
                return JavaValue.Void;
            })
            .Method("deallocate", "()V", static (_, args) =>
            {
                var self = args.Self<JavaPlayer>();
                self.Clip?.Stop();
                if (self.State > Realized) self.State = Realized;
                return JavaValue.Void;
            })
            .Method("getState", "()I", static (_, args) => JavaValue.Int(args.Self<JavaPlayer>().State))
            .Method("setLoopCount", "(I)V", static (_, args) =>
            {
                args.Self<JavaPlayer>().LoopCount = args.Int(0);
                return JavaValue.Void;
            })
            .Method("getDuration", "()J",
                static (_, args) => JavaValue.Long(args.Self<JavaPlayer>().Clip?.Duration ?? -1))
            .Method("getMediaTime", "()J", static (_, _) => JavaValue.Long(-1))
            .Method("setMediaTime", "(J)J", static (_, args) => JavaValue.Long(args.Long(0)))
            .Method("getContentType", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaPlayer>().ContentType))
            .NoOpMethod("addPlayerListener", "(Ljavax/microedition/media/PlayerListener;)V")
            .NoOpMethod("removePlayerListener", "(Ljavax/microedition/media/PlayerListener;)V")
            .Method("getControl", "(Ljava/lang/String;)Ljavax/microedition/media/Control;",
                static (thread, args) =>
                {
                    string? name = args.String(0);
                    if (name is null || !name.Contains("Volume", StringComparison.Ordinal))
                        return JavaValue.Null;

                    JavaObject control = thread.Vm.NewInstance(
                        thread.Vm.LoadClass("javax/microedition/media/control/VolumeControlImpl"));
                    control.SetField(0, JavaValue.Ref(args.This));
                    return JavaValue.Ref(control);
                })
            .Method("getControls", "()[Ljavax/microedition/media/Control;", static (thread, _) =>
                JavaValue.Ref(thread.Vm.NewArray(
                    thread.Vm.LoadClass("[Ljavax/microedition/media/Control;"), 0))));
    }

    private static void RegisterControls(RuntimeRegistry registry)
    {
        registry.DefineInterface("javax/microedition/media/Control");

        registry.DefineInterface("javax/microedition/media/control/VolumeControl", builder => builder
            .InterfaceMethod("setLevel", "(I)I")
            .InterfaceMethod("getLevel", "()I")
            .InterfaceMethod("setMute", "(Z)V")
            .InterfaceMethod("isMuted", "()Z"));

        registry.Define("javax/microedition/media/control/VolumeControlImpl", builder => builder
            .Implements("javax/microedition/media/control/VolumeControl", "javax/microedition/media/Control")
            .Field("player", "Ljavax/microedition/media/Player;")
            .Field("muted", "Z")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("setLevel", "(I)I", static (_, args) =>
            {
                int level = Math.Clamp(args.Int(0), 0, 100);
                if (args.This.GetField(0).Reference is JavaPlayer { Clip: { } clip }) clip.Volume = level;
                return JavaValue.Int(level);
            })
            .Method("getLevel", "()I", static (_, args) =>
                JavaValue.Int(args.This.GetField(0).Reference is JavaPlayer { Clip: { } clip } ? clip.Volume : 100))
            .Method("setMute", "(Z)V", static (_, args) =>
            {
                args.This.SetField(1, JavaValue.Bool(args.Bool(0)));
                if (args.This.GetField(0).Reference is JavaPlayer { Clip: { } clip })
                    clip.Volume = args.Bool(0) ? 0 : 100;
                return JavaValue.Void;
            })
            .Method("isMuted", "()Z", static (_, args) => args.This.GetField(1)));

        registry.DefineInterface("javax/microedition/media/control/ToneControl", builder => builder
            .Constant("VERSION", -2)
            .Constant("TEMPO", -3)
            .Constant("RESOLUTION", -4)
            .Constant("BLOCK_START", -5)
            .Constant("BLOCK_END", -6)
            .Constant("PLAY_BLOCK", -7)
            .Constant("SET_VOLUME", -8)
            .Constant("REPEAT", -9)
            .Constant("SILENCE", -1)
            .Constant("C4", 60)
            .InterfaceMethod("setSequence", "([B)V"));
    }
}
