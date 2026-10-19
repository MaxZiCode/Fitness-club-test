using DataAccessLayer.Entities;

namespace ServiceLayer.Enrollments;

public sealed record CreateEnrollmentResult(
    long Id,
    long ClassId,
    int Seats,
    string Status,
    DateTime CreatedAt)
{
    public static CreateEnrollmentResult FromEnrollment(Enrollment enrollment) =>
        new(enrollment.Id, enrollment.ClassId, enrollment.Seats, enrollment.Status, enrollment.CreatedAt);
}
