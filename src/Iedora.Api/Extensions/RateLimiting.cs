using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Iedora.Api;

/// <summary>Request throttling limits (config section <c>RateLimiting</c>). Tests raise these so the
/// suite isn't throttled; prod uses the defaults or overrides them.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Sensitive auth endpoints (login/register/forgot/token/reset/change), per client IP.</summary>
    public int AuthPermitLimit { get; set; } = 20;

    /// <summary>Image uploads (POST /media/images), per authenticated user.</summary>
    public int UploadPermitLimit { get; set; } = 30;

    /// <summary>Blanket per-IP DoS net over everything else (generous — a busy venue on one NAT is fine).</summary>
    public int GlobalPermitLimit { get; set; } = 600;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Per-IP / per-user rate limiting. Centralized in the host (not per-module) so there's no shared
/// policy-name const to thread across modules: one chained limiter classifies by path/method.
/// The brute-force / enumeration targets (auth) and image uploads get tight limits; everything else
/// gets a generous blanket net.
/// </summary>
public static class RateLimitingExtensions
{
    // The brute-force / enumeration targets. NOT refresh (periodic), whoami/sessions/commands
    // (authenticated reads + status polling — throttling those would fight normal clients).
    private static readonly HashSet<string> AuthSensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/login", "/auth/register", "/auth/forgot-password",
        "/auth/token", "/auth/reset-password", "/auth/change-password",
    };

    public static WebApplicationBuilder AddIedoraRateLimiter(this WebApplicationBuilder builder)
    {
        var o = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        var window = TimeSpan.FromSeconds(o.WindowSeconds);

        // Behind a reverse proxy, honor X-Forwarded-For ONLY from configured proxy IPs, so the real
        // client IP drives partitioning and nobody can spoof it (empty list ⇒ only loopback trusted).
        builder.Services.Configure<ForwardedHeadersOptions>(fh =>
        {
            fh.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var p in (builder.Configuration["ForwardedHeaders:KnownProxies"] ?? "")
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (IPAddress.TryParse(p, out var ip)) fh.KnownProxies.Add(ip);
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Chained: a request must pass every limiter. Non-matching requests get a no-op partition,
            // so each link only throttles the surface it owns.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    AuthSensitive.Contains(ctx.Request.Path.Value ?? "")
                        ? RateLimitPartition.GetFixedWindowLimiter($"auth:{Ip(ctx)}",
                            _ => new FixedWindowRateLimiterOptions { PermitLimit = o.AuthPermitLimit, Window = window })
                        : RateLimitPartition.GetNoLimiter("_")),

                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    HttpMethods.IsPost(ctx.Request.Method)
                        && string.Equals(ctx.Request.Path.Value, "/media/images", StringComparison.OrdinalIgnoreCase)
                        ? RateLimitPartition.GetFixedWindowLimiter($"upload:{User(ctx)}",
                            _ => new FixedWindowRateLimiterOptions { PermitLimit = o.UploadPermitLimit, Window = window })
                        : RateLimitPartition.GetNoLimiter("_")),

                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    RateLimitPartition.GetSlidingWindowLimiter($"all:{Ip(ctx)}",
                        _ => new SlidingWindowRateLimiterOptions { PermitLimit = o.GlobalPermitLimit, Window = window, SegmentsPerWindow = 6 })));
        });

        return builder;
    }

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
    private static string User(HttpContext ctx) => ctx.User.FindFirst("sub")?.Value ?? Ip(ctx);
}
