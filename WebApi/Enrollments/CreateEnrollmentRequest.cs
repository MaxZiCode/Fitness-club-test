using System.ComponentModel.DataAnnotations;
using ServiceLayer.Enrollments;

namespace WebApi.Enrollments;

public sealed record CreateEnrollmentRequest(
    [Required] long? ClassId,
    [Required, Range(1, int.MaxValue)] int? Seats)
{
    public CreateEnrollmentCommand ToCommand(string idempotencyKey, long userId) =>
        new(ClassId!.Value, Seats!.Value, idempotencyKey, userId);
}
