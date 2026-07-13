using Bunit;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class StaffPagesTests : MudBunitContext
{
    private IIedoraApiv1 RegisterApi()
    {
        var api = Substitute.For<IIedoraApiv1>();
        Services.AddSingleton(api);
        return api;
    }

    private static StaffRestaurantRow Row(string name, string slug) =>
        new() { Id = Guid.NewGuid(), Name = name, Slug = slug, Menus = 2, Items = 40, Views30d = 900 };

    [TestMethod]
    public void Directory_lists_restaurants_linking_to_their_detail()
    {
        var api = RegisterApi();
        var row = Row("Tasca", "tasca");
        api.StaffDirectory(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StaffDirectoryResponse { Restaurants = { row } });

        var cut = Render<Iedora.Dashboard.Components.Pages.Directory>();

        Assert.IsTrue(cut.Markup.Contains("Tasca"));
        Assert.IsTrue(cut.Markup.Contains($"/restaurants/{row.Id}"), "row should link to the restaurant detail");
    }

    [TestMethod]
    public void Alerts_shows_the_counts_and_the_stale_list()
    {
        var api = RegisterApi();
        api.StaffAlerts(Arg.Any<CancellationToken>()).Returns(new StaffAlertsResponse
        {
            UnboundQr = 5,
            StaleRestaurants = { Row("Stale Diner", "stale") },
        });

        var cut = Render<Alerts>();

        CollectionAssert.Contains(cut.FindAll(".tile-value").Select(e => e.TextContent).ToList(), "5");
        Assert.IsTrue(cut.Markup.Contains("Stale Diner"));
        Assert.IsTrue(cut.Markup.Contains("Every restaurant has dishes."), "empty-menus list should show its empty message");
    }

    [TestMethod]
    public void Users_lists_users_linking_to_their_detail()
    {
        var api = RegisterApi();
        var user = new AdminUserView { Id = Guid.NewGuid(), Email = "a@b.pt", Name = "Ana", Roles = { "admin" } };
        api.StaffListUsers(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new StaffUserListResponse { Users = { user } });

        var cut = Render<Users>();

        Assert.IsTrue(cut.Markup.Contains("a@b.pt"));
        Assert.IsTrue(cut.Markup.Contains("admin"));
        Assert.IsTrue(cut.Markup.Contains($"/users/{user.Id}"), "row should link to the user detail");
    }

    [TestMethod]
    public void A_failed_call_shows_the_error_message()
    {
        var api = RegisterApi();
        api.StaffAlerts(Arg.Any<CancellationToken>()).Returns<StaffAlertsResponse>(_ => throw new HttpRequestException("down"));

        var cut = Render<Alerts>();

        Assert.IsTrue(cut.Markup.Contains("Couldn't reach the API", StringComparison.OrdinalIgnoreCase));
    }
}
