using System.Net;

namespace Iedora.Dashboard;

/// <summary>Maps a failed API call to a message the staff pages show. A 401 only reaches here if a
/// token refresh already failed (the BearerHandler retries first), so it means "re-authenticate".</summary>
public static class ApiError
{
    public static string Describe(Exception ex) =>
        ex is Refit.ApiException { StatusCode: HttpStatusCode.Unauthorized }
            ? "Your session expired — please sign out and back in."
            : "Couldn't reach the API. Try again in a moment.";
}
