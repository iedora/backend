using Iedora.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Drives the Menu module's dedup-marker sweeps against the real Postgres schema: seed a mix of
// stale + fresh markers, run the sweep at a fixed clock, and assert only the expired ones are gone.
[TestClass]
public sealed class DedupRetentionTests : IntegrationTestBase
{
    // A fixed "now" so the seeded markers straddle each sweep's retention cutoff deterministically.
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Clock = new(Now);

    private static async Task<T> WithDb<T>(Func<MenuDbContext, Task<T>> body)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        return await body(scope.ServiceProvider.GetRequiredService<MenuDbContext>());
    }

    private static Restaurant NewRestaurant(Guid id) => new()
    {
        Id = id, TenantId = Guid.NewGuid(), Name = "Tasca", Slug = $"r-{id:N}",
        DefaultLanguage = "en", SupportedLanguages = ["en"], DefaultCurrency = "EUR",
    };

    [TestMethod]
    public async Task View_seen_sweep_prunes_only_markers_older_than_the_retention_window()
    {
        var rest = Guid.CreateVersion7();
        // Default ViewSeenHours = 3, so the cutoff is 09:00Z. Seed one just-inside and one well-outside.
        var fresh = Now.AddHours(-1);   // 11:00Z — kept
        var stale = Now.AddHours(-10);  // 02:00Z — pruned
        await WithDb(async db =>
        {
            db.Restaurants.Add(NewRestaurant(rest));
            db.ViewSeen.Add(new ViewSeen { VisitorId = Guid.NewGuid(), RestaurantId = rest, HourStart = fresh });
            db.ViewSeen.Add(new ViewSeen { VisitorId = Guid.NewGuid(), RestaurantId = rest, HourStart = stale });
            await db.SaveChangesAsync();
            return 0;
        });

        var removed = await WithDb(db =>
            new ViewSeenSweep(db, Clock, Options.Create(new DedupRetentionOptions())).SweepAsync(default));

        Assert.AreEqual(1, removed);
        var survivors = await WithDb(db => db.ViewSeen.Where(v => v.RestaurantId == rest).Select(v => v.HourStart).ToListAsync());
        CollectionAssert.AreEqual(new[] { fresh }, survivors); // only the fresh marker remains
    }

    [TestMethod]
    public async Task Item_view_seen_sweep_prunes_only_days_before_the_retention_window()
    {
        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();
        var item = Guid.CreateVersion7();
        // Default ItemViewSeenDays = 2, so days < 2026-07-07 are pruned.
        var today = new DateOnly(2026, 7, 9);
        var withinWindow = today.AddDays(-1); // 07-08 — kept
        var expired = today.AddDays(-5);      // 07-04 — pruned
        await WithDb(async db =>
        {
            db.Restaurants.Add(NewRestaurant(restId));
            db.Menus.Add(new Menu { Id = menuId, RestaurantId = restId, Name = "Lunch", Position = 0, Active = true });
            db.Categories.Add(new Category { Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters", Position = 0 });
            db.Items.Add(new Item { Id = item, CategoryId = catId, RestaurantId = restId, Name = "Soup", PriceCents = 500, Currency = "EUR", Position = 0, Available = true });
            db.ItemViewSeen.Add(new ItemViewSeen { VisitorId = Guid.NewGuid(), ItemId = item, Day = withinWindow });
            db.ItemViewSeen.Add(new ItemViewSeen { VisitorId = Guid.NewGuid(), ItemId = item, Day = expired });
            await db.SaveChangesAsync();
            return 0;
        });

        var removed = await WithDb(db =>
            new ItemViewSeenSweep(db, Clock, Options.Create(new DedupRetentionOptions())).SweepAsync(default));

        Assert.AreEqual(1, removed);
        var survivors = await WithDb(db => db.ItemViewSeen.Where(v => v.ItemId == item).Select(v => v.Day).ToListAsync());
        CollectionAssert.AreEqual(new[] { withinWindow }, survivors);
    }
}
