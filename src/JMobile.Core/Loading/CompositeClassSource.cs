namespace JMobile.Core.Loading;

/// <summary>
/// Consults several sources in order and answers with the first hit.
/// </summary>
/// <remarks>
/// Suites are occasionally split across a main JAR plus a folder of patched or localised
/// resources. Composing sources keeps that case out of the loader, which continues to see a
/// single <see cref="IClassSource"/>.
/// </remarks>
public sealed class CompositeClassSource(params IClassSource[] sources) : IClassSource
{
    private readonly IClassSource[] _sources = sources;

    public string Name => string.Join(" + ", _sources.Select(source => source.Name));

    public IReadOnlyList<IClassSource> Sources => _sources;

    public bool TryGetClassBytes(string internalName, out byte[] bytes)
    {
        foreach (IClassSource source in _sources)
        {
            if (source.TryGetClassBytes(internalName, out bytes)) return true;
        }

        bytes = [];
        return false;
    }

    public bool TryGetResource(string path, out byte[] bytes)
    {
        foreach (IClassSource source in _sources)
        {
            if (source.TryGetResource(path, out bytes)) return true;
        }

        bytes = [];
        return false;
    }

    public IEnumerable<string> EnumerateEntries() => _sources.SelectMany(source => source.EnumerateEntries());
}

/// <summary>An empty source, used when a VM is created before any suite has been chosen.</summary>
public sealed class EmptyClassSource : IClassSource
{
    public static readonly EmptyClassSource Instance = new();

    private EmptyClassSource() { }

    public string Name => "<none>";

    public bool TryGetClassBytes(string internalName, out byte[] bytes)
    {
        bytes = [];
        return false;
    }

    public bool TryGetResource(string path, out byte[] bytes)
    {
        bytes = [];
        return false;
    }

    public IEnumerable<string> EnumerateEntries() => [];
}
