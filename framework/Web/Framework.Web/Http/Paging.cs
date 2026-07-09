namespace Framework.Web;

/// <summary>Shared page-size bounds for list endpoints so no single request can pull an unbounded
/// result set. A list endpoint takes the client's optional <c>limit</c>/<c>offset</c> and runs them
/// through <see cref="Clamp"/> before touching the database.</summary>
public static class Paging
{
    /// <summary>Rows returned when the client gives no limit.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Hard ceiling on rows per request — a client may ask for fewer, never more.</summary>
    public const int MaxLimit = 200;

    /// <summary>Hard ceiling on how far a client may page in (a deep offset scans everything skipped).</summary>
    public const int MaxOffset = 10_000;

    /// <summary>Clamp a client's optional <paramref name="limit"/>/<paramref name="offset"/> into safe
    /// <c>(Take, Skip)</c> bounds.</summary>
    public static (int Take, int Skip) Clamp(int? limit, int? offset = null) =>
        (Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), Math.Clamp(offset ?? 0, 0, MaxOffset));
}
