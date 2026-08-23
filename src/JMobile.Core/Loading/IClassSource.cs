namespace JMobile.Core.Loading;

/// <summary>
/// Where the virtual machine goes to find class bytes and resource files.
/// </summary>
/// <remarks>
/// Keeping this behind an interface is what lets a MIDlet suite in a JAR, a folder of loose
/// class files produced by a build, and an in-memory suite synthesised by a test all be
/// consumed by the same VM with no special cases in the loader.
/// </remarks>
public interface IClassSource
{
    /// <summary>A short label used in diagnostics, e.g. the JAR file name.</summary>
    string Name { get; }

    /// <summary>Looks up a class by internal name, e.g. <c>com/example/Game</c>.</summary>
    bool TryGetClassBytes(string internalName, out byte[] bytes);

    /// <summary>
    /// Looks up a resource by suite-relative path. A leading slash is optional; MIDlets pass
    /// both forms to <c>Class.getResourceAsStream</c> and expect both to work.
    /// </summary>
    bool TryGetResource(string path, out byte[] bytes);

    /// <summary>Every resource path this source can serve, used for suite inspection.</summary>
    IEnumerable<string> EnumerateEntries();
}
