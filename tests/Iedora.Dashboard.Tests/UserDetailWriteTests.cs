using Bunit;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class UserDetailWriteTests : BunitContext
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

    [TestMethod]
    public async Task Set_password_posts_the_typed_password()
    {
        var api = RegisterApi();
        api.StaffSetUserPassword(UserId, Arg.Any<SetUserPasswordRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = RenderDetail();
        cut.Find(".inline-form input").Change("Temp1234!");
        cut.Find(".inline-form").Submit();

        await api.Received().StaffSetUserPassword(UserId, Arg.Is<SetUserPasswordRequest>(r => r.Password == "Temp1234!"), Arg.Any<CancellationToken>());
        Assert.IsTrue(cut.Markup.Contains("must change it at next login"));
    }

    [TestMethod]
    public async Task Revoke_calls_the_api_for_that_session_family()
    {
        var api = RegisterApi();
        api.StaffRevokeUserSession(UserId, Family, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = RenderDetail();
        cut.Find("button.danger").Click();

        await api.Received().StaffRevokeUserSession(UserId, Family, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Force_password_change_runs_when_confirmed()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var api = RegisterApi();
        api.StaffForcePasswordChange(UserId, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cut = RenderDetail();
        cut.FindAll("button.btn").First(b => b.TextContent.Contains("Force")).Click();

        await api.Received().StaffForcePasswordChange(UserId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Force_password_change_is_skipped_when_cancelled()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var api = RegisterApi();

        var cut = RenderDetail();
        cut.FindAll("button.btn").First(b => b.TextContent.Contains("Force")).Click();

        api.DidNotReceive().StaffForcePasswordChange(UserId, Arg.Any<CancellationToken>());
    }
}
