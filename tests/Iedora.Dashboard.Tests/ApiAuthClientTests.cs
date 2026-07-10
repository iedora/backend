using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class ApiAuthClientTests
{
    private static (ApiAuthClient api, TestHttp.Stub stub) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new TestHttp.Stub(respond);
        return (new ApiAuthClient(new TestHttp.Factory(stub)), stub);
    }

    [TestMethod]
    public async Task Login_posts_to_auth_login_and_returns_the_access_token()
    {
        var (api, stub) = Build(_ => TestHttp.Token("acc-1"));

        var token = await api.LoginAsync("a@b.pt", "pw", CancellationToken.None);

        Assert.AreEqual("acc-1", token);
        Assert.AreEqual("/auth/login", stub.Last!.RequestUri!.AbsolutePath);
    }

    [TestMethod]
    public async Task Refresh_posts_to_auth_refresh_and_returns_the_rotated_token()
    {
        var (api, stub) = Build(_ => TestHttp.Token("acc-2"));

        var token = await api.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("acc-2", token);
        Assert.AreEqual("/auth/refresh", stub.Last!.RequestUri!.AbsolutePath);
    }

    [TestMethod]
    public async Task A_failed_auth_returns_null()
    {
        var (api, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.IsNull(await api.LoginAsync("a@b.pt", "bad", CancellationToken.None));
    }
}
