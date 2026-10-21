using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLayer.Schedule;
using WebApi.Schedule;

namespace WebApi.Controllers;

[ApiController]
[Authorize]
[Route("schedule")]
public sealed class ScheduleController(IScheduleService scheduleService) : BaseApiController
{
    private static readonly IReadOnlyDictionary<string, string> ErrorCodeToParamName =
        new Dictionary<string, string>
        {
            [ScheduleErrors.InvalidContinuationToken.Code] = nameof(ScheduleRequest.ContinuationToken)
        };

    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] ScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.ToQuery(CurrentUserId);

        var result = await scheduleService.GetScheduleAsync(query, cancellationToken);
        return result.Match(
            page => Ok(ScheduleResponse.FromPage(page)),
            errors => HandleErrors(errors, ErrorCodeToParamName));
    }
}
