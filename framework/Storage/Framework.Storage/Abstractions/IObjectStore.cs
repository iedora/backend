namespace Framework.Storage;

/// <summary>Stores opaque blobs under forward-slash keys and hands out their public addresses.
/// Transport only — one local-disk implementation today; an S3/R2/MinIO one drops in behind the
/// same interface without the callers (or the stored URLs) changing.</summary>
public interface IObjectStore
{
    /// <summary>Write (or overwrite) the object at <paramref name="key"/>.</summary>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct);

    /// <summary>Best-effort delete — an orphaned object is cheap; a failed delete must not throw.</summary>
    void Delete(string key);

    /// <summary>Open the object for reading, or null if it doesn't exist (or the key is invalid).</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>The public (browser/CDN) address of a key.</summary>
    string PublicUrl(string key);

    /// <summary>Invert <see cref="PublicUrl"/>; null when the URL isn't one of ours (so a caller can
    /// refuse to act on a foreign URL).</summary>
    string? KeyFromPublicUrl(string url);
}
