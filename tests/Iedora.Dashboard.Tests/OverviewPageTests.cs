using System.Net;
using Bunit;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Refit;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class OverviewPageTests : MudBunitContext
{
    private IIedoraApiv1 RegisterApi()
    {
        var api = Substitute.For<IIedoraApiv1>();
        Services.AddSingleton(api);
        return api;
    }

    [TestMethod]
    public void Renders_the_totals_and_the_top_restaurants()
    {
        var api = RegisterApi();
        api.StaffOverview(Arg.Any<CancellationToken>()).Returns(new StaffOverviewResponse
        {
            Restaurants = 12, ActiveMenus = 9, Items = 340, ViewsToday = 55, Views30d = 1200, QrBound = 8, QrUnbound = 3,
            TopByViews = { new StaffRestaurantRow { Name = "Tasca", Slug = "tasca", Menus = 2, Items = 40, Views30d = 900 } },
        });

        var cut = Render<Home>();

        var tiles = cut.FindAll(".tile-value").Select(e => e.TextContent).ToList();
        CollectionAssert.Contains(tiles, "12");   // restaurants
        CollectionAssert.Contains(tiles, "1200"); // 30-day views
        CollectionAssert.Contains(tiles, "3");    // unbound QR

        Assert.IsTrue(cut.Markup.Contains("Tasca"), "top restaurant row missing");
        Assert.IsTrue(cut.Markup.Contains("900"), "row's 30-day views missing");
    }

    [TestMethod]
    public async Task Shows_a_session_message_when_the_api_returns_401()
    {
        var api = RegisterApi();
        var unauthorized = await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "https://api/staff/overview"), HttpMethod.Get,
            new HttpResponseMessage(HttpStatusCode.Unauthorized), new RefitSettings());
        api.StaffOverview(Arg.Any<CancellationToken>()).Returns(Task.FromException<StaffOverviewResponse>(unauthorized));

        var cut = Render<Home>();

        Assert.IsTrue(cut.Markup.Contains("session expired", StringComparison.OrdinalIgnoreCase),
            "expected the expired-session message");
    }
}
