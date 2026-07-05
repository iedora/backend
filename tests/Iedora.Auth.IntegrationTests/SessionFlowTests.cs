using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Iedora.Auth.IntegrationTests;

public sealed class SessionFlowTests(AuthApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        await Register("a@tasca.pt", "Sup3rSecret!");
        var (_, c1) = await Login("a@tasca.pt", "Sup3rSecret!");

        var resp = await Refresh(c1);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var c2 = RefreshCookieFrom(resp);
        Assert.NotNull(c2);
        Assert.NotEqual(c1, c2);                              // rotation ⇒ a brand-new token
        var body = (await resp.Content.ReadFromJsonAsync<TokenPayload>())!;
        Assert.NotEmpty(body.accessToken);
    }

    [Fact]
    public async Task Refresh_without_cookie_returns_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(refreshToken: null!)).StatusCode);
        // (no Cookie header at all)
        var bare = await Client.PostAsync("/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, bare.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_burns_the_whole_family()
    {
        await Register("victim@tasca.pt", "Sup3rSecret!");
        var (_, c1) = await Login("victim@tasca.pt", "Sup3rSecret!");

        // Legit rotation: c1 → c2.
        var rotated = await Refresh(c1);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var c2 = RefreshCookieFrom(rotated)!;

        // Replay the spent c1 → reuse detected, 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(c1)).StatusCode);

        // …and the family is burned, so the previously-valid c2 is now dead too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(c2)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_clears_the_cookie()
    {
        await Register("bye@tasca.pt", "Sup3rSecret!");
        var (_, c1) = await Login("bye@tasca.pt", "Sup3rSecret!");

        var logout = await Logout(c1);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Null(RefreshCookieFrom(logout));               // cookie cleared (empty value)

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(c1)).StatusCode);
    }

    [Fact]
    public async Task Logout_is_idempotent_for_an_unknown_token()
    {
        var resp = await Logout("this-is-not-a-real-token");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Logout_all_revokes_every_session_of_the_user()
    {
        await Register("multi@tasca.pt", "Sup3rSecret!");
        var (session1, c1) = await Login("multi@tasca.pt", "Sup3rSecret!"); // device 1
        var (_, c2) = await Login("multi@tasca.pt", "Sup3rSecret!");        // device 2

        var logoutAll = await PostBearer("/auth/logout-all", session1.accessToken);
        Assert.Equal(HttpStatusCode.OK, logoutAll.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(c1)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(c2)).StatusCode);
    }
}
