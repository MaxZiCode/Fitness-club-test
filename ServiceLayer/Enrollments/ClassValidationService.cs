using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class ClassValidationService : IClassValidationService
{
    public ErrorOr<Success> Validate(
        Class @class,
        int bookedSeats,
        int seatsDiff,
        DateTime now)
    {
        if (@class.IsCancelled)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassCancelled);

        if (@class.StartsAt <= now)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassStarted);

        if (seatsDiff > 0)
        {
            if (@class.Capacity < bookedSeats + seatsDiff)
                return Error.Conflict(EnrollmentErrors.Conflict.CapacityExceeded);
        }

        return Result.Success;
    }
}
