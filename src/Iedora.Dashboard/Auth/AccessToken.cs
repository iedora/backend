namespace Iedora.Dashboard;

/// <summary>Request-scoped holder for the admin's API access token. Set during login (so the
/// whoami round-trip carries the bearer); the <see cref="BearerHandler"/> reads it when calling the
/// API. Re-hydrating it from the auth cookie for interactive component calls comes with the first
/// data page.</summary>
public sealed class AccessToken
{
    public string? Value { get; set; }
}
