using DataAccessLayer.Entities;

namespace WebApi.Enrollments;

public sealed record CreateEnrollmentResponse(
    long Id,
    long ClassId,
    int Seats,
    string Status,
    DateTime CreatedAt)
{
    public static CreateEnrollmentResponse FromEnrollment(Enrollment enrollment) =>
        new(enrollment.Id, enrollment.ClassId, enrollment.Seats, enrollment.Status, enrollment.CreatedAt);
}
