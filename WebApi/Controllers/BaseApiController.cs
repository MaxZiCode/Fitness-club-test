using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using ServiceLayer.Idempotency;

namespace WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected const string AdminRole = "admin";

    protected long CurrentUserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    protected bool IsAdmin => User.IsInRole(AdminRole);

    protected IActionResult HandleErrors(List<Error> errors)
    {
        var error = errors[0];

        if (error.Code == IdempotencyErrors.PayloadMismatch.Code)
            return GetUnprocessableContentResult();

        return error.Type switch
        {
            ErrorType.Validation => GetValidationProblemResult(errors),
            ErrorType.Conflict => GetConflictResult(error.Code),
            ErrorType.NotFound => GetNotFoundResult(),
            _ => GetInternalServerErrorResult()
        };
    }

    private ActionResult GetValidationProblemResult(List<Error> errors)
    {
        errors.ForEach(e => ModelState.AddModelError(e.Code, e.Description));
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

    private ObjectResult GetUnprocessableContentResult()
    {
        return Problem(statusCode: 422);
    }

    private ObjectResult GetInternalServerErrorResult()
    {
        return Problem(statusCode: 500);
    }
}
