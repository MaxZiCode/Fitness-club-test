using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class EnrollmentValidationService : IEnrollmentValidationService
{
    public ErrorOr<Success> Validate(Enrollment enrollment)
    {
        if (enrollment.Status == EnrollmentStatuses.Cancelled)
            return EnrollmentErrors.EnrollmentCancelled;

        return Result.Success;
    }
}
