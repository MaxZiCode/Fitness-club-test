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
        return ValidateClassState(@class, now)
            .ThenEnsure(_ => ValidateEnrollmentCapacity(@class, bookedSeats, seatsDiff));
    }

    public ErrorOr<Success> ValidateWaitlist(
        Class @class,
        int bookedSeats,
        int requestedSeats,
        DateTime now)
    {
        return ValidateClassState(@class, now)
            .ThenEnsure(_ => ValidateWaitlistCapacity(@class, bookedSeats, requestedSeats));
    }

    private static ErrorOr<Success> ValidateClassState(Class @class, DateTime now)
    {
        if (@class.IsCancelled)
            return ClassErrors.ClassCancelled;

        if (@class.StartsAt <= now)
            return ClassErrors.ClassStarted;

        return Result.Success;
    }

    private static ErrorOr<Success> ValidateEnrollmentCapacity(
        Class @class,
        int bookedSeats,
        int seatsDiff)
    {
        if (seatsDiff > 0 && seatsDiff > @class.Capacity - bookedSeats)
            return ClassErrors.CapacityExceeded;

        return Result.Success;
    }

    private static ErrorOr<Success> ValidateWaitlistCapacity(
        Class @class,
        int bookedSeats,
        int requestedSeats)
    {
        if (requestedSeats <= @class.Capacity - bookedSeats)
            return WaitlistErrors.SeatsAvailable;

        return Result.Success;
    }
}
