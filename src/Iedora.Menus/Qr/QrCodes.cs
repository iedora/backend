using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Iedora.Menus;

/// <summary>Physical QR sticker code helpers (ported from qr.ts). Codes are a cross-tenant registry;
/// normalized lowercase, 1-64 chars of <c>[a-z0-9_-]</c>.</summary>
public static partial class QrCodes
{
    public const int MaxBulk = 500;
    private const int GeneratedLength = 8;

    // Crockford-flavoured base32 minus lookalikes (0/O, 1/I/L, U): codes survive being read aloud.
    private const string Alphabet = "abcdefghjkmnpqrstvwxyz23456789";

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex CodePattern();

    /// <summary>Canonicalize operator input.</summary>
    public static string Normalize(string raw) => (raw ?? "").Trim().ToLowerInvariant();

    /// <summary>True if a normalized code has an acceptable shape.</summary>
    public static bool IsValid(string code) => CodePattern().IsMatch(code);

    /// <summary>Mint a random sticker code (~39 bits; the PK uniqueness check is the final collision
    /// guard).</summary>
    public static string Generate()
    {
        Span<byte> buf = stackalloc byte[GeneratedLength];
        RandomNumberGenerator.Fill(buf);
        var chars = new char[GeneratedLength];
        for (var i = 0; i < GeneratedLength; i++) chars[i] = Alphabet[buf[i] % Alphabet.Length];
        return new string(chars);
    }
}
