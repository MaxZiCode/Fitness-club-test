using ServiceLayer.Enrollments;

namespace WebApi.Enrollments;

public sealed record CreateEnrollmentResponse(
    long Id,
    long ClassId,
    int Seats,
    string Status,
    DateTime CreatedAt)
{
    public static CreateEnrollmentResponse FromResult(CreateEnrollmentResult result) =>
        new(result.Id, result.ClassId, result.Seats, result.Status, result.CreatedAt);
}
