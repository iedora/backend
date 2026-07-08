using System.Security.Cryptography;
using ErrorOr;
using Framework.Storage;
using Microsoft.AspNetCore.Http;

namespace Framework.Media;

// Validate an uploaded image and store it. We sniff the magic bytes and derive the extension from
// what the file ACTUALLY is — so a script renamed .png can't be stored as one. The key is random-
// slugged (defeats CDN/browser caching of a replaced asset) under the caller's tenant prefix.
internal static class ImageValidation
{
    public const long MiB = 1 << 20;
    public const long MaxImageBytes = 5 * MiB; // covers logos, banners and dish photos

    private enum ImageKind { Jpeg, Png, Webp }

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Buffer the upload, enforce size + a real image signature, and store it under
    /// <paramref name="keyPrefix"/>. Returns the stored object's public URL.</summary>
    public static async Task<ErrorOr<string>> StoreAsync(
        IObjectStore store, string keyPrefix, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return MediaErrors.EmptyUpload;
        if (file.Length > MaxImageBytes) return MediaErrors.ImageTooLarge;

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

        if (Sniff(bytes.Span) is not { } kind) return MediaErrors.UnsupportedImage;

        var key = $"{keyPrefix}/{RandomSlug()}.{Extension(kind)}";
        await store.PutAsync(key, bytes, ct);
        return store.PublicUrl(key);
    }

    // Detect the image type from its leading bytes (nothing else is accepted).
    private static ImageKind? Sniff(ReadOnlySpan<byte> b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? ImageKind.Jpeg
        : b.Length >= 8 && b[..8].SequenceEqual(PngMagic) ? ImageKind.Png
        : b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8) ? ImageKind.Webp
        : null;

    private static string Extension(ImageKind kind) => kind switch
    {
        ImageKind.Jpeg => "jpg",
        ImageKind.Png => "png",
        ImageKind.Webp => "webp",
        _ => "bin",
    };

    private static string RandomSlug() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
}
