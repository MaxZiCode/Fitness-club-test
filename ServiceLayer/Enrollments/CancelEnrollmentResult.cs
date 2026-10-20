using DataAccessLayer.Entities;

namespace ServiceLayer.Enrollments;

public sealed record CancelEnrollmentResult(long Id, string Status, int RefundedVisits)
{
    public static CancelEnrollmentResult FromEnrollment(Enrollment enrollment) =>
        new(enrollment.Id, enrollment.Status, enrollment.RefundedVisits);
}
