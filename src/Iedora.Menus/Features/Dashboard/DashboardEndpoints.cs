using System.Security.Claims;
using Iedora.Identity.Contracts;
using Framework.Web;

namespace Iedora.Menus;

public sealed record MonthlyViewsResponse(int Count);

// The owner dashboard, mounted under /api/dashboard (authenticated, tenant-scoped). Every read
// aggregates over the caller's own tenant — there is no slug here, the tenant comes from the token.
public static class DashboardEndpoints
{
    public static void MapDashboard(this RouteGroupBuilder dashboard)
    {
        // GET /api/dashboard/analytics?range=today|7d|30d — scans, content state, top dishes, dwell.
        dashboard.MapGet("/analytics", async (
                string? range, ClaimsPrincipal user, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (user.TenantId() is not { } tenant) return ProblemResults.From(MenuErrors.TenantRequired);
            if (!DashboardAnalytics.TryRange(range, out var days)) return ProblemResults.From(MenuErrors.UnknownRange);

            var analytics = await DashboardAnalytics.BuildAsync(db, tenant, range!, days, clock.GetUtcNow(), ct);
            return Results.Ok(analytics);
        })
        .WithName("DashboardAnalytics").WithSummary("Tenant scan analytics + content state for a range.")
        .Produces<AnalyticsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/dashboard/views/month — the tenant's total menu views this calendar month.
        dashboard.MapGet("/views/month", async (
                ClaimsPrincipal user, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (user.TenantId() is not { } tenant) return ProblemResults.From(MenuErrors.TenantRequired);

            var count = await DashboardAnalytics.MonthlyViewsAsync(db, tenant, clock.GetUtcNow(), ct);
            return Results.Ok(new MonthlyViewsResponse(count));
        })
        .WithName("DashboardMonthlyViews").WithSummary("The tenant's menu views this calendar month.")
        .Produces<MonthlyViewsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
