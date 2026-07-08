using System.Security.Claims;
using Framework.Storage;
using Framework.Web;
using Microsoft.AspNetCore.Http;

namespace Framework.Media;

public sealed record ImageUploadResponse(string PublicUrl);

// POST /media/images — authenticated, tenant-scoped image upload. The bytes land under the caller's
// tenant (t/{tenantId}/…); the caller gets a URL back and attaches it to its own resource. This
// service never learns what the image is FOR.
public static class UploadEndpoint
{
    public static void MapImageUpload(this IEndpointRouteBuilder app)
    {
        app.MapPost("/media/images", async (
                IFormFile file, ClaimsPrincipal user, IObjectStore store, CancellationToken ct) =>
        {
            // Tenant-scope the key by the "tid" claim (read directly so this stays domain-agnostic).
            if (!Guid.TryParse(user.FindFirst("tid")?.Value, out var tenant))
                return ProblemResults.From(MediaErrors.TenantRequired);

            var stored = await ImageValidation.StoreAsync(store, $"t/{tenant}", file, ct);
            return stored.IsError
                ? ProblemResults.From(stored.Errors)
                : Results.Ok(new ImageUploadResponse(stored.Value));
        })
        .RequireAuthorization().DisableAntiforgery()
        .WithName("UploadImage").WithSummary("Upload an image; returns its public URL (tenant-scoped).")
        .Produces<ImageUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
