using Iedora.Auth.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Features.Register;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record RegisteredResponse(Guid Id, string Email);

// POST /auth/register — create an account via Identity's UserManager (its PasswordHasher +
// validators). Identity's ValidationErrors map straight to an RFC 9457 ValidationProblem.
public static class RegisterEndpoint
{
    public static void MapRegister(this RouteGroupBuilder group) =>
        group.MapPost("/register",
            async Task<Results<Created<RegisteredResponse>, ValidationProblem>> (
                RegisterRequest req, UserManager<AppUser> users) =>
        {
            var user = new AppUser { UserName = req.Email, Email = req.Email, DisplayName = req.DisplayName };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
                return TypedResults.ValidationProblem(errors);
            }
            return TypedResults.Created($"/auth/users/{user.Id}", new RegisteredResponse(user.Id, user.Email!));
        })
        .WithName("Register")
        .WithSummary("Create a new account.");
}
