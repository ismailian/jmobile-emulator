namespace JMobile.Core.Loading;

/// <summary>
/// Serves classes and resources from a directory tree, which is what a build output or an
/// already-extracted suite looks like.
/// </summary>
public sealed class DirectoryClassSource : IClassSource
{
    private readonly string _root;

    public DirectoryClassSource(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Name = Path.GetFileName(_root.TrimEnd(Path.DirectorySeparatorChar));
    }

    public string Name { get; }

    public bool TryGetClassBytes(string internalName, out byte[] bytes) =>
        TryRead(internalName + ".class", out bytes);

    public bool TryGetResource(string path, out byte[] bytes) => TryRead(path, out bytes);

    private bool TryRead(string relativePath, out byte[] bytes)
    {
        bytes = [];

        string trimmed = relativePath.Replace('\\', '/').TrimStart('/');
        if (trimmed.Length == 0) return false;

        string full = Path.GetFullPath(Path.Combine(_root, trimmed));

        // A suite must not be able to reach outside its own directory via "..".
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return false;
        if (!File.Exists(full)) return false;

        bytes = File.ReadAllBytes(full);
        return true;
    }

    public IEnumerable<string> EnumerateEntries()
    {
        if (!Directory.Exists(_root)) yield break;

        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            yield return Path.GetRelativePath(_root, file).Replace('\\', '/');
    }
}
