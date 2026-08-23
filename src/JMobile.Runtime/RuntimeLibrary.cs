using JMobile.Core.Native;
using JMobile.Runtime.Io;
using JMobile.Runtime.Lang;
using JMobile.Runtime.Media;
using JMobile.Runtime.Midp;
using JMobile.Runtime.Util;

namespace JMobile.Runtime;

/// <summary>
/// The composition root of the class library: which modules make up a CLDC 1.1 / MIDP 2.0
/// environment.
/// </summary>
/// <remarks>
/// Order does not matter. The registry stores definitions as callbacks and builds a class only
/// when it is first resolved, so a module may reference a class another module contributes.
/// </remarks>
public static class RuntimeLibrary
{
    /// <summary>Everything a MIDlet suite expects to find.</summary>
    public static IReadOnlyList<IRuntimeModule> Midp20 { get; } =
    [
        new LangModule(),
        new StringModule(),
        new WrapperModule(),
        new UtilModule(),
        new IoModule(),
        new ConnectorModule(),
        new GraphicsModule(),
        new LcduiModule(),
        new FormsModule(),
        new GameModule(),
        new RmsModule(),
        new MediaModule(),
        new VendorModule(),
    ];

    /// <summary>
    /// The subset that does not need a display, for tests and for tooling that only has to load
    /// and execute code.
    /// </summary>
    public static IReadOnlyList<IRuntimeModule> Headless { get; } =
    [
        new LangModule(),
        new StringModule(),
        new WrapperModule(),
        new UtilModule(),
        new IoModule(),
    ];
}
