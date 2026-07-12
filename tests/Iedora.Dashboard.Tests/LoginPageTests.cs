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
    // ── LoginForm: EditForm + DataAnnotations ────────────────────────────────
    [TestMethod]
    public void Submitting_valid_credentials_reports_them()
    {
        (string Email, string Password)? captured = null;
        var cut = Render<LoginForm>(p => p.Add(x => x.OnSubmit,
            EventCallback.Factory.Create<(string Email, string Password)>(this, c => captured = c)));

        cut.Find("input[type=email]").Change("a@b.pt");       // InputText binds on change
        cut.Find("input[type=password]").Change("pw");
        cut.Find("form").Submit();

        Assert.AreEqual("a@b.pt", captured?.Email);
        Assert.AreEqual("pw", captured?.Password);
    }

    [TestMethod]
    public void An_invalid_email_is_rejected_client_side_and_does_not_submit()
    {
        (string, string)? captured = null;
        var cut = Render<LoginForm>(p => p.Add(x => x.OnSubmit,
            EventCallback.Factory.Create<(string, string)>(this, c => captured = c)));

        cut.Find("input[type=email]").Change("not-an-email");
        cut.Find("input[type=password]").Change("pw");
        cut.Find("form").Submit();

        Assert.IsNull(captured, "OnValidSubmit must not fire when the email is invalid");
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
        cut.Find("input[type=email]").Change("a@b.pt");
        cut.Find("input[type=password]").Change("Sup3rSecret!");
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
