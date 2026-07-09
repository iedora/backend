using System.Security.Claims;
using Iedora.Dashboard.Api;
using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Refit;

namespace Iedora.Dashboard;

// The dashboard's own cookie sign-in/-out. Login is a static form POST (a cookie can only be set on a
// real HTTP response, not over the interactive circuit), backed by the API's /auth/login.
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /login — verify credentials with the API, require the platform-admin role, then open
        // the dashboard's cookie session (stashing the API token in the auth ticket for later calls).
        app.MapPost("/login", async (
            [FromForm] string email, [FromForm] string password,
            IIedoraApiv1 api, AccessToken token, HttpContext http, CancellationToken ct) =>
        {
            TokenResponse tokens;
            try
            {
                tokens = await api.Login(new LoginRequest { Email = email, Password = password }, ct);
            }
            catch (ApiException)
            {
                return Results.Redirect("/login?error=invalid"); // 401 (bad credentials) or similar
            }

            token.Value = tokens.AccessToken; // so the whoami call below is authenticated
            var me = await api.WhoAmI(ct);
            if (me.Roles is null || !me.Roles.Contains(Roles.Admin))
                return Results.Redirect("/login?error=forbidden"); // not a platform admin — no access

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, me.UserId),
                new(ClaimTypes.Name, me.Email ?? email),
            };
            claims.AddRange(me.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var props = new AuthenticationProperties { IsPersistent = true };
            props.StoreTokens([new AuthenticationToken { Name = "access_token", Value = tokens.AccessToken }]);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                props);
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
