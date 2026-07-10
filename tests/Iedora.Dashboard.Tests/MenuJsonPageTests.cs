using Bunit;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class MenuJsonPageTests : BunitContext
{
    private static readonly Guid RestId = Guid.NewGuid();

    private IIedoraApiv1 RegisterApi()
    {
        var api = Substitute.For<IIedoraApiv1>();
        var doc = new MenuImportDocument { Menus = { new ImportMenu { Name = "Lunch" } } };
        api.StaffExportMenus(RestId, Arg.Any<CancellationToken>()).Returns(doc);
        Services.AddSingleton(api);
        return api;
    }

    private IRenderedComponent<MenuJson> RenderPage() => Render<MenuJson>(p => p.Add(x => x.Id, RestId));

    [TestMethod]
    public void Export_fills_the_editor_with_the_menu_json()
    {
        RegisterApi();

        var cut = RenderPage();

        Assert.IsTrue(cut.Markup.Contains("Lunch"), "the exported menu should appear in the editor");
        Assert.IsTrue(cut.Markup.Contains("menus"), "editor should hold the JSON document");
    }

    [TestMethod]
    public async Task Import_replaces_from_the_editor_json_when_confirmed()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var api = RegisterApi();
        api.StaffReplaceMenus(RestId, Arg.Any<MenuImportDocument>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = RenderPage(); // export populated the editor with valid JSON
        cut.FindAll("button.btn").First(b => b.TextContent.Contains("Import")).Click();

        await api.Received().StaffReplaceMenus(RestId, Arg.Any<MenuImportDocument>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(cut.Markup.Contains("Menu replaced"));
    }

    [TestMethod]
    public void Invalid_json_is_rejected_without_calling_the_api()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var api = RegisterApi();

        var cut = RenderPage();
        cut.Find("textarea.json").Input("this is not json");
        cut.FindAll("button.btn").First(b => b.TextContent.Contains("Import")).Click();

        Assert.IsTrue(cut.Markup.Contains("isn't valid JSON", StringComparison.OrdinalIgnoreCase));
        api.DidNotReceive().StaffReplaceMenus(RestId, Arg.Any<MenuImportDocument>(), Arg.Any<CancellationToken>());
    }
}
