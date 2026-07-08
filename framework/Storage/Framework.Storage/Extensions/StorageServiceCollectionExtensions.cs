using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>Register the local-disk <see cref="IObjectStore"/>, reading <c>Storage:Root</c> and
    /// <c>Storage:PublicBasePath</c> from config (root defaults to a temp dir for dev/test). Idempotent
    /// — safe for several modules that share the same store to each call it.</summary>
    public static IServiceCollection AddLocalDiskStorage(this IServiceCollection services, IConfiguration config)
    {
        var root = config["Storage:Root"];
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Path.GetTempPath(), "iedora-media");
        var basePath = config["Storage:PublicBasePath"];
        if (string.IsNullOrWhiteSpace(basePath)) basePath = "/media";
        Directory.CreateDirectory(root);

        services.TryAddSingleton(new StorageOptions { Root = root, PublicBasePath = basePath });
        services.TryAddSingleton<IObjectStore, LocalDiskObjectStore>();
        return services;
    }
}
