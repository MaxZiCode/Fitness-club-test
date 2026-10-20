using System.ComponentModel.DataAnnotations;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Enrollments;
using ServiceLayer.Enrollments;

namespace WebApi.Controllers;

[ApiController]
[Authorize]
[Route("enrollments")]
public class EnrollmentsController(
    ICreateEnrollmentService createEnrollmentService,
    IEnrollmentCardService enrollmentCardService,
    IUpdateEnrollmentSeatsService updateEnrollmentSeatsService,
    ICancelEnrollmentService cancelEnrollmentService) : BaseApiController
{
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetCardAsync(long id, CancellationToken cancellationToken)
    {
        var command = new GetEnrollmentCardCommand(id, CurrentUserId);

        return await enrollmentCardService.GetEnrollmentCardAsync(command, cancellationToken)
            .Match(card => Ok(EnrollmentCardResponse.FromCard(card)), HandleErrors);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateEnrollmentRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = request.ToCommand(idempotencyKey, CurrentUserId);

        return await createEnrollmentService.CreateEnrollmentAsync(command, cancellationToken)
            .Match(
                result => CreatedAtAction(
                    "GetCard",
                    new { id = result.Id },
                    CreateEnrollmentResponse.FromResult(result)),
                HandleErrors);
    }

    [HttpPost("{id:long}/seats")]
    public async Task<IActionResult> UpdateSeatsAsync(
        long id,
        [FromBody] UpdateEnrollmentSeatsRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.ToCommand(id, CurrentUserId);

        return await updateEnrollmentSeatsService.UpdateSeatsAsync(command, cancellationToken)
            .Match(card => Ok(EnrollmentCardResponse.FromCard(card)), HandleErrors);
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> CancelAsync(long id, CancellationToken cancellationToken)
    {
        var command = new CancelEnrollmentCommand(id, CurrentUserId);

        return await cancelEnrollmentService.CancelEnrollmentAsync(command, cancellationToken)
            .Match(result => Ok(CancelEnrollmentResponse.FromResult(result)), HandleErrors);
    }
}
