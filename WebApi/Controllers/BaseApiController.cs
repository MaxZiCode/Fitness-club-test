using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected const string AdminRole = "admin";

    protected long CurrentUserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    protected bool IsAdmin => User.IsInRole(AdminRole);

    protected IActionResult HandleErrors(List<Error> errors) => HandleErrors(errors, null);

    protected IActionResult HandleErrors(
        List<Error> errors,
        IReadOnlyDictionary<string, string>? errorCodeToParamName)
    {
        var error = errors[0];

        return error.Type switch
        {
            ErrorType.Validation => GetValidationProblemResult(errors, errorCodeToParamName),
            ErrorType.Conflict => GetConflictResult(error.Code),
            ErrorType.NotFound => GetNotFoundResult(),
            _ => GetInternalServerErrorResult()
        };
    }

    private ActionResult GetValidationProblemResult(
        List<Error> errors,
        IReadOnlyDictionary<string, string>? errorCodeToParamName)
    {
        if (errorCodeToParamName is not null)
        {
            errors.ForEach(error =>
            {
                if (errorCodeToParamName.TryGetValue(error.Code, out var paramName))
                    ModelState.AddModelError(paramName, error.Description);
            });
        }


        return ValidationProblem();
    }

    private ObjectResult GetNotFoundResult()
    {
        return Problem(statusCode: 404);
    }

    private ObjectResult GetConflictResult(string reason)
    {
        return Problem(statusCode: 409, extensions: new Dictionary<string, object?>() { { "reason", reason } });
    }

    private ObjectResult GetInternalServerErrorResult()
    {
        return Problem(statusCode: 500);
    }
}
