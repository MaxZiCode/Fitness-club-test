using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class PassValidationService : IPassValidationService
{
    public ErrorOr<Success> Validate(Pass pass, int requestedSeats, DateTime now)
    {
        if (pass.ValidUntil <= DateOnly.FromDateTime(now))
            return Error.Conflict(EnrollmentErrors.Conflict.PassExpired);

        if (pass.RemainingVisits < requestedSeats)
            return Error.Conflict(EnrollmentErrors.Conflict.PassExhausted);

        return Result.Success;
    }
}
