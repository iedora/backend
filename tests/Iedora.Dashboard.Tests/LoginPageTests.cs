using System.Net;
using System.Text.Json;
using Bunit;
using Iedora.Dashboard.Components;
using Iedora.Dashboard.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class LoginPageTests : BunitContext
{
    // ── LoginForm: the render-once hardening ─────────────────────────────────
    [TestMethod]
    public void The_form_renders_once_and_ignores_later_state_changes()
    {
        var cut = Render<LoginForm>(p => p.Add(x => x.OnSubmit, EventCallback.Factory.Create<(string, string)>(this, _ => { })));

        Assert.AreEqual(1, cut.RenderCount);
        cut.Find("input[type=email]").Input("typing"); // @bind:oninput → StateHasChanged
        Assert.AreEqual(1, cut.RenderCount, "the form must not re-render, so Blazor never re-diffs extension-mutated DOM");
    }

    [TestMethod]
    public void Submitting_the_form_reports_the_typed_credentials()
    {
        (string Email, string Password)? captured = null;
        var cut = Render<LoginForm>(p => p.Add(x => x.OnSubmit,
            EventCallback.Factory.Create<(string Email, string Password)>(this, c => captured = c)));

        cut.Find("input[type=email]").Input("a@b.pt");
        cut.Find("input[type=password]").Input("pw");
        cut.Find("form").Submit();

        Assert.AreEqual("a@b.pt", captured?.Email);
        Assert.AreEqual("pw", captured?.Password);
    }

    // ── Login page: the flow around the form ─────────────────────────────────
    private static string TokenWith(object payload)
    {
        static string Seg(object o) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(o))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Seg(new { alg = "ES256" })}.{Seg(payload)}.sig";
    }

    private TokenStore RegisterAuth(string? loginToken)
    {
        var stub = new TestHttp.Stub(req =>
            req.RequestUri!.AbsolutePath == "/auth/login" && loginToken is not null
                ? TestHttp.Token(loginToken)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var tokens = new TokenStore();
        var auth = new ApiAuthClient(new TestHttp.Factory(stub));
        Services.AddSingleton(tokens);
        Services.AddSingleton(auth);
        Services.AddSingleton(new ApiAuthStateProvider(tokens, auth));
        return tokens;
    }

    private void SignIn(IRenderedComponent<Login> cut)
    {
        cut.Find("input[type=email]").Input("a@b.pt");
        cut.Find("input[type=password]").Input("Sup3rSecret!");
        cut.Find("form").Submit();
    }

    [TestMethod]
    public void An_admin_login_signs_in()
    {
        var adminToken = TokenWith(new { sub = "u", email = "a@b.pt", roles = new[] { "admin" } });
        var tokens = RegisterAuth(adminToken);

        SignIn(Render<Login>());

        Assert.AreEqual(adminToken, tokens.AccessToken); // signed in with the token
    }

    [TestMethod]
    public void Bad_credentials_show_an_error_and_do_not_sign_in()
    {
        var tokens = RegisterAuth(loginToken: null); // /auth/login → 401

        var cut = Render<Login>();
        SignIn(cut);

        Assert.IsNull(tokens.AccessToken);
        Assert.IsTrue(cut.Markup.Contains("Incorrect email or password"));
    }

    [TestMethod]
    public void A_non_admin_is_rejected()
    {
        var tokens = RegisterAuth(TokenWith(new { sub = "u", email = "a@b.pt", roles = new[] { "staff" } }));

        var cut = Render<Login>();
        SignIn(cut);

        Assert.IsNull(tokens.AccessToken); // not signed in
        Assert.IsTrue(cut.Markup.Contains("isn't a platform admin"));
    }
}
