using ErrorOr;

namespace Framework.Media;

/// <summary>The Media service's error catalog (module-private; the type selects the HTTP status via
/// ProblemResults).</summary>
internal static class MediaErrors
{
    public static readonly Error TenantRequired = Error.Validation(
        "media.tenant_required", "Uploading needs a tenant-scoped token.");

    public static readonly Error EmptyUpload = Error.Validation(
        "media.empty_upload", "No file was uploaded.");

    public static readonly Error UnsupportedImage = Error.Validation(
        "media.unsupported_image", "The upload must be a JPEG, PNG or WebP image.");

    public static readonly Error ImageTooLarge = Error.Validation(
        "media.image_too_large", "The image exceeds the size limit.");
}
