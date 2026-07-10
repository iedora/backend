using Bunit;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class QrCodesPageTests : BunitContext
{
    private IIedoraApiv1 RegisterApi(params QrCodeView[] codes)
    {
        var api = Substitute.For<IIedoraApiv1>();
        var response = new QrCodeListResponse { Total = codes.Length };
        foreach (var c in codes) response.Codes.Add(c);
        api.ListQrCodes(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(response);
        Services.AddSingleton(api);
        return api;
    }

    [TestMethod]
    public void Lists_the_stickers()
    {
        RegisterApi(new QrCodeView { Code = "table-1", Label = "" });

        var cut = Render<QrCodes>();

        Assert.IsTrue(cut.Markup.Contains("table-1"));
    }

    [TestMethod]
    public async Task Mint_calls_create_with_the_requested_count()
    {
        var api = RegisterApi();
        api.CreateQrCodes(Arg.Any<CreateQrRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQrResponse { Inserted = 10 });

        var cut = Render<QrCodes>();
        cut.Find("form.mint").Submit();

        await api.Received().CreateQrCodes(Arg.Is<CreateQrRequest>(r => r.Count == 10), Arg.Any<CancellationToken>());
        Assert.IsTrue(cut.Markup.Contains("Minted 10"));
    }

    [TestMethod]
    public async Task Delete_calls_the_api_for_that_code()
    {
        var api = RegisterApi(new QrCodeView { Code = "gone" });
        api.DeleteQrCode("gone", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = Render<QrCodes>();
        cut.Find("button.danger").Click();

        await api.Received().DeleteQrCode("gone", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Bind_without_a_valid_id_shows_an_error_and_does_not_call_the_api()
    {
        var api = RegisterApi(new QrCodeView { Code = "unbound" }); // RestaurantId null → shows the bind input

        var cut = Render<QrCodes>();
        cut.FindAll("button.link").First(b => b.TextContent.Trim() == "Bind").Click();

        Assert.IsTrue(cut.Markup.Contains("valid restaurant id", StringComparison.OrdinalIgnoreCase));
        api.DidNotReceive().BindQrCode(Arg.Any<string>(), Arg.Any<BindQrRequest>(), Arg.Any<CancellationToken>());
    }
}
