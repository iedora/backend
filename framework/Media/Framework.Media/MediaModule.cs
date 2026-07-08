using Framework.Storage;

namespace Framework.Media;

/// <summary>
/// The <b>Media</b> service: a generic, domain-agnostic image store. It authenticates the uploader,
/// validates + stores the bytes, and serves them back — it knows nothing about restaurants, menus or
/// which column a URL ends up in. Owning modules upload here, then attach the returned URL to their
/// own resource. Storage sits behind <see cref="IObjectStore"/> (local disk today; S3/R2/MinIO later).
/// </summary>
public static class MediaModule
{
    public static IHostApplicationBuilder AddMediaModule(this IHostApplicationBuilder builder)
    {
        builder.Services.AddLocalDiskStorage(builder.Configuration);
        return builder;
    }

    public static IEndpointRouteBuilder MapMediaModule(this IEndpointRouteBuilder app)
    {
        app.MapImageUpload(); // POST /media/images  (authenticated, tenant-scoped)
        app.MapMediaServe();  // GET  /media/{**key} (anonymous)
        return app;
    }
}
