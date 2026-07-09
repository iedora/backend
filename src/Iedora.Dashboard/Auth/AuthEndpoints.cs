using Iedora.Dashboard.Api;
using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Iedora.Dashboard;

// The dashboard's own cookie sign-in/-out. Login is a static form POST (a cookie can only be set on a
// real HTTP response, not over the interactive circuit), backed by the API's /auth/login + /auth/whoami.
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /login — verify credentials with the API (capturing the refresh cookie), require the
        // platform-admin role, then open the dashboard's cookie session with the access + refresh tokens.
        app.MapPost("/login", async (
            [FromForm] string email, [FromForm] string password,
            AuthApi auth, IIedoraApiv1 api, AccessToken token, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(email, password, ct);
            if (result is null) return Results.Redirect("/login?error=invalid");

            token.Value = result.AccessToken; // authenticate the whoami call below
            WhoAmIResponse me;
            try { me = await api.WhoAmI(ct); }
            catch { return Results.Redirect("/login?error=invalid"); }

            if (me.Roles is null || !me.Roles.Contains(Roles.Admin))
                return Results.Redirect("/login?error=forbidden"); // not a platform admin — no access

            var claims = TokenRefresh.BuildClaims(me.UserId, me.Email ?? email, me.Roles, result);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect("/");
        }).AllowAnonymous();

        // POST /logout — clear the cookie session.
        app.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }
}
