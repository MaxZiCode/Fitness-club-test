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
public class EnrollmentsController(ICreateEnrollmentService createEnrollmentService) : BaseApiController
{
    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateEnrollmentRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await createEnrollmentService.CreateEnrollmentAsync(request.ToCommand(idempotencyKey, CurrentUserId), cancellationToken)
            .Match(enrollment => Created("", CreateEnrollmentResponse.FromEnrollment(enrollment)), HandleErrors);
    }
}
