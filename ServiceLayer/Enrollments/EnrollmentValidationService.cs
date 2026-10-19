using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class EnrollmentValidationService : IEnrollmentValidationService
{
    public ErrorOr<Success> Validate(Enrollment enrollment)
    {
        if (enrollment.Status == EnrollmentStatuses.Cancelled)
            return Error.Conflict(EnrollmentErrors.Conflict.EnrollmentCancelled);

        return Result.Success;
    }
}
