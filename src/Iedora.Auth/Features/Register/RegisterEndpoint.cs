using System.ComponentModel.DataAnnotations;
using Iedora.Auth.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Features.Register;

public sealed record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, StringLength(128, MinimumLength = 8)] string Password,
    string? DisplayName);
public sealed record RegisteredResponse(Guid Id, string Email);

// POST /auth/register — shape-validated by the built-in minimal-API validation (AddValidation),
// then created via Identity's UserManager (its PasswordHasher + policy validators). Identity's
// domain errors map to an RFC 9457 ValidationProblem.
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
