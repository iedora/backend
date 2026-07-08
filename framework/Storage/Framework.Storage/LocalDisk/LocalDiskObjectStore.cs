namespace Framework.Storage;

// Stores objects as files under a root directory and serves them under PublicBasePath. Keys are
// forward-slash paths; every path that touches the filesystem is re-rooted and bounds-checked, so a
// crafted key (absolute, or with '..') can neither read nor write outside the root.
public sealed class LocalDiskObjectStore(StorageOptions options) : IObjectStore
{
    private readonly string _root = Path.GetFullPath(options.Root);
    private readonly string _base = options.PublicBasePath.TrimEnd('/');

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        if (!TryMap(key, out var path)) throw new InvalidOperationException($"invalid object key '{key}'");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct);
    }

    public void Delete(string key)
    {
        try { if (TryMap(key, out var path) && File.Exists(path)) File.Delete(path); }
        catch { /* orphans are cheap; correctness is not */ }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        if (!TryMap(key, out var path) || !File.Exists(path)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public string PublicUrl(string key) => $"{_base}/{key}";

    public string? KeyFromPublicUrl(string url)
    {
        var prefix = $"{_base}/";
        return url.StartsWith(prefix, StringComparison.Ordinal) ? url[prefix.Length..] : null;
    }

    // Map a key to an absolute path, rejecting absolute keys and any '..' that escapes the root.
    private bool TryMap(string key, out string absolutePath)
    {
        absolutePath = "";
        if (string.IsNullOrEmpty(key)) return false;
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (full != _root && !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;
        absolutePath = full;
        return true;
    }
}
