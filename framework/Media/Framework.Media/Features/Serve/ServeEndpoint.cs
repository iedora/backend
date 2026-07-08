using Framework.Storage;

namespace Framework.Media;

// GET /media/{**key} — serve a stored object to anyone (guests see menu images). Keys are random-
// slugged, so a response is immutable and long-cacheable. Traversal is refused inside the store.
public static class ServeEndpoint
{
    public static void MapMediaServe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media/{**key}", async (string key, HttpContext http, IObjectStore store, CancellationToken ct) =>
        {
            var stream = await store.OpenReadAsync(key, ct);
            if (stream is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Stream(stream, ContentTypeForKey(key));
        })
        .AllowAnonymous().WithName("ServeMedia").WithSummary("Serve a stored media object.")
        .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
    }

    // The content type a served file advertises, from its (trusted, self-generated) extension.
    private static string ContentTypeForKey(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
