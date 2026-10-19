using System.ComponentModel.DataAnnotations;
using ServiceLayer.Enrollments;

namespace WebApi.Enrollments;

public sealed record UpdateEnrollmentSeatsRequest(
    [Required, Range(1, int.MaxValue)] int? Seats)
{
    public UpdateEnrollmentSeatsCommand ToCommand(long enrollmentId, long userId) =>
        new(enrollmentId, Seats.GetValueOrDefault(), userId);
}
