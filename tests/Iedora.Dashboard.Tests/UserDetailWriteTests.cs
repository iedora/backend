using Bunit;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MudBlazor;
using NSubstitute;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class UserDetailWriteTests : MudBunitContext
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Family = Guid.NewGuid();

    private IIedoraApiv1 RegisterApi()
    {
        var api = Substitute.For<IIedoraApiv1>();
        var detail = new StaffUserDetail
        {
            User = new AdminUserView { Id = UserId, Email = "a@b.pt", Name = "Ana", Roles = { "admin" } },
        };
        detail.Sessions.Add(new UserSessionRecord
        {
            Id = Guid.NewGuid(), FamilyId = Family, Ip = "1.2.3.4", UserAgent = "Chrome", IssuedAt = DateTimeOffset.UtcNow,
        });
        api.StaffUserDetail(UserId, Arg.Any<CancellationToken>()).Returns(detail);
        Services.AddSingleton(api);
        return api;
    }

    private IRenderedComponent<UserDetail> RenderDetail() =>
        Render<UserDetail>(p => p.Add(x => x.Id, UserId));

    private static void Click<T>(IRenderedComponent<T> scope, string text) where T : IComponent =>
        scope.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [TestMethod]
    public async Task Set_password_posts_the_typed_password()
    {
        var api = RegisterApi();
        api.StaffSetUserPassword(UserId, Arg.Any<SetUserPasswordRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = RenderDetail();
        cut.Find("input[type=password]").Input("Temp1234!"); // MudTextField Immediate binds oninput
        Click(cut, "Set password");

        await api.Received().StaffSetUserPassword(UserId, Arg.Is<SetUserPasswordRequest>(r => r.Password == "Temp1234!"), Arg.Any<CancellationToken>());
        Assert.IsTrue(cut.Markup.Contains("must change it at next login"));
    }

    [TestMethod]
    public async Task Revoke_calls_the_api_for_that_session_family()
    {
        var api = RegisterApi();
        api.StaffRevokeUserSession(UserId, Family, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = RenderDetail();
        Click(cut, "Revoke");

        await api.Received().StaffRevokeUserSession(UserId, Family, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Force_password_change_runs_when_confirmed()
    {
        var api = RegisterApi(); // register all services before the first render
        api.StaffForcePasswordChange(UserId, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var dialog = Render<MudDialogProvider>();

        var cut = RenderDetail();
        Click(cut, "Force password change");
        Click(dialog, "Force"); // confirm the message box

        await api.Received().StaffForcePasswordChange(UserId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Force_password_change_is_skipped_when_cancelled()
    {
        var api = RegisterApi(); // register all services before the first render
        var dialog = Render<MudDialogProvider>();

        var cut = RenderDetail();
        Click(cut, "Force password change");
        Click(dialog, "Cancel"); // dismiss the message box

        api.DidNotReceive().StaffForcePasswordChange(UserId, Arg.Any<CancellationToken>());
    }
}
