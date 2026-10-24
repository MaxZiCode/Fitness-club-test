using System.ComponentModel.DataAnnotations;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLayer.Enrollments.Waitlist;
using WebApi.Enrollments.Waitlist;

namespace WebApi.Controllers;

[ApiController]
[Authorize]
[Route("enrollments/waitlist")]
public class WaitlistController(
    IJoinWaitlistService joinWaitlistService,
    ICancelWaitlistService cancelWaitlistService) : BaseApiController
{
    [HttpPost]
    public async Task<IActionResult> JoinAsync(
        [FromBody] JoinWaitlistRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = request.ToCommand(idempotencyKey, CurrentUserId);

        return await joinWaitlistService.JoinWaitlistAsync(command, cancellationToken)
            .Match(
                result => StatusCode(201, JoinWaitlistResponse.FromResult(result)),
                HandleErrors);
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> CancelAsync(long id, CancellationToken cancellationToken)
    {
        var command = new CancelWaitlistCommand(id, CurrentUserId);

        return await cancelWaitlistService.CancelWaitlistAsync(command, cancellationToken)
            .Match(result => Ok(CancelWaitlistResponse.FromResult(result)), HandleErrors);
    }
}
