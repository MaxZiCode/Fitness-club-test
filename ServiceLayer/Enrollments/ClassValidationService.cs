using DataAccessLayer.Entities;
using ErrorOr;
using ServiceLayer.Enrollments.Waitlist;

namespace ServiceLayer.Enrollments;

public sealed class ClassValidationService : IClassValidationService
{
    public ErrorOr<Success> ValidateEnrollment(
        Class @class,
        int bookedSeats,
        int seatsDiff,
        DateTime now)
    {
        var classStateResult = ValidateClassState(@class, now);
        if (classStateResult.IsError)
            return classStateResult.Errors;

        return ValidateEnrollmentCapacity(@class, bookedSeats, seatsDiff);
    }

    public ErrorOr<Success> ValidateWaitlist(
        Class @class,
        int bookedSeats,
        int requestedSeats,
        DateTime now)
    {
        var classStateResult = ValidateClassState(@class, now);
        if (classStateResult.IsError)
            return classStateResult.Errors;

        return ValidateWaitlistCapacity(@class, bookedSeats, requestedSeats);
    }

    private static ErrorOr<Success> ValidateClassState(Class @class, DateTime now)
    {
        if (@class.IsCancelled)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassCancelled);

        if (@class.StartsAt <= now)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassStarted);

        return Result.Success;
    }

    private static ErrorOr<Success> ValidateEnrollmentCapacity(
        Class @class,
        int bookedSeats,
        int seatsDiff)
    {
        if (seatsDiff > 0 && seatsDiff > @class.Capacity - bookedSeats)
            return Error.Conflict(EnrollmentErrors.Conflict.CapacityExceeded);

        return Result.Success;
    }

    private static ErrorOr<Success> ValidateWaitlistCapacity(
        Class @class,
        int bookedSeats,
        int requestedSeats)
    {
        if (requestedSeats <= @class.Capacity - bookedSeats)
            return Error.Conflict(WaitlistErrors.Conflict.SeatsAvailable);

        return Result.Success;
    }
}
