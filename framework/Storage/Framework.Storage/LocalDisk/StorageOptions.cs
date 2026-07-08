namespace Framework.Storage;

/// <summary>Where local-disk objects live and how they're addressed. Bound from the <c>Storage</c>
/// config section; <see cref="Root"/> defaults to a temp dir when unset (dev/test) — point it at a
/// mounted volume in prod.</summary>
public sealed class StorageOptions
{
    public string Root { get; set; } = "";
    public string PublicBasePath { get; set; } = "/media";
}
