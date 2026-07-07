using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Framework.Web.Tests;

[TestClass]
public sealed class ProblemResultsTests
{
    [TestMethod]
    public void Unauthorized_maps_to_401_with_the_code_extension_and_detail()
    {
        var result = (ProblemHttpResult)ProblemResults.From(Error.Unauthorized("auth.invalid", "nope"));

        Assert.AreEqual(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.AreEqual("auth.invalid", result.ProblemDetails.Extensions["code"]);
        Assert.AreEqual("nope", result.ProblemDetails.Detail);
    }

    [TestMethod]
    public void Error_type_selects_the_status_code()
    {
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ProblemHttpResult)ProblemResults.From(Error.Forbidden("c", "d"))).StatusCode);
        Assert.AreEqual(StatusCodes.Status404NotFound, ((ProblemHttpResult)ProblemResults.From(Error.NotFound("c", "d"))).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((ProblemHttpResult)ProblemResults.From(Error.Conflict("c", "d"))).StatusCode);
    }

    [TestMethod]
    public void All_validation_errors_become_a_400_validation_problem_grouped_by_code()
    {
        var result = (ValidationProblem)ProblemResults.From(
            [Error.Validation("a", "A one"), Error.Validation("a", "A two"), Error.Validation("b", "B")]);

        Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("a", result.ProblemDetails.Errors.Keys);
        Assert.Contains("b", result.ProblemDetails.Errors.Keys);
        Assert.HasCount(2, result.ProblemDetails.Errors["a"]); // grouped by code
    }

    [TestMethod]
    public void A_mixed_error_set_uses_the_first_error_status_not_validation()
    {
        var result = (ProblemHttpResult)ProblemResults.From(
            [Error.Unauthorized("u", "U"), Error.Validation("v", "V")]);

        Assert.AreEqual(StatusCodes.Status401Unauthorized, result.StatusCode);
    }
}
