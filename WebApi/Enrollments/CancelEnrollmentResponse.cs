using ServiceLayer.Enrollments;

namespace WebApi.Enrollments;

public sealed record CancelEnrollmentResponse(long Id, string Status, int RefundedVisits)
{
    public static CancelEnrollmentResponse FromResult(CancelEnrollmentResult result) =>
        new(result.Id, result.Status, result.RefundedVisits);
}
