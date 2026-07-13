using Iedora.Dashboard;
using Iedora.Dashboard.Api;
using Iedora.Dashboard.Components;
using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Refit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// MudBlazor design system (dialogs, snackbar, popovers, theming).
builder.Services.AddMudServices();

// The API base URL — same for auth and data, from wwwroot/appsettings*.json (a deploy sets its own),
// falling back to the app's own origin. Must be HTTPS cross-origin: the refresh cookie is
// SameSite=None; Secure, which browsers only send over TLS (dev runs the API on https://localhost:8091).
var apiBase = new Uri(builder.Configuration["Api:BaseUrl"] ?? builder.HostEnvironment.BaseAddress);

// Client-side auth: the access token lives in memory; the refresh token is the API's HttpOnly cookie,
// held by the browser. This is exactly how the front-office will consume the API — no server session.
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<ApiAuthClient>();
builder.Services.AddScoped<ApiAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthStateProvider>());
builder.Services.AddAuthorizationCore(options =>
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().RequireRole(Roles.Admin).Build());
builder.Services.AddCascadingAuthenticationState();

// Auth calls go with browser credentials so the API's HttpOnly refresh cookie is stored/replayed.
builder.Services.AddHttpClient("auth", c => c.BaseAddress = apiBase);

// Data client (generated): carries the bearer and refreshes once on a 401.
builder.Services.AddTransient<BearerHandler>();
builder.Services.AddRefitClient<IIedoraApiv1>()
    .ConfigureHttpClient(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BearerHandler>();

await builder.Build().RunAsync();
