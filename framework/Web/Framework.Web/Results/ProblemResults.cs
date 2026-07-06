using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Framework.Web;

/// <summary>
/// Maps <see cref="Error"/> values to RFC 9457 <c>ProblemDetails</c> (rendered by the app's
/// <c>AddProblemDetails</c> pipeline). All-validation error sets become a single
/// <c>ValidationProblem</c> (400, grouped by code); otherwise the first error's <see cref="ErrorType"/>
/// selects the status, and the error <c>code</c> rides along in a <c>code</c> extension so the
/// client gets a machine-readable discriminator in the problem body.
/// </summary>
public static class ProblemResults
{
    public static IResult From(Error error) => Single(error);

    public static IResult From(List<Error> errors)
    {
        if (errors.Count == 0) return TypedResults.Problem();
        if (errors.All(e => e.Type == ErrorType.Validation)) return Validation(errors);
        return Single(errors[0]);
    }

    private static ProblemHttpResult Single(Error error) =>
        TypedResults.Problem(
            detail: error.Description,
            statusCode: error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            },
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    private static ValidationProblem Validation(List<Error> errors)
    {
        var byCode = new Dictionary<string, string[]>();
        foreach (var e in errors)
            byCode[e.Code] = byCode.TryGetValue(e.Code, out var existing)
                ? [.. existing, e.Description]
                : [e.Description];
        return TypedResults.ValidationProblem(byCode);
    }
}
