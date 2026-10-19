using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class ClassValidationService(IUnitOfWork unitOfWork) : IClassValidationService
{
    public async Task<ErrorOr<Success>> ValidateAsync(
        long classId,
        int requestedSeats,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var @class = await unitOfWork.Class
            .GetLockedNoTrackingIgnoringFiltersWithEnrollmentsAsync(classId, cancellationToken);
        if (@class is null)
            return Error.NotFound();

        if (@class.IsCancelled)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassCancelled);

        if (@class.StartsAt <= now)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassStarted);

        var bookedSeats = @class.Enrollments.Sum(enrollment => enrollment.Seats);
        if (@class.Capacity < bookedSeats + requestedSeats)
            return Error.Conflict(EnrollmentErrors.Conflict.CapacityExceeded);

        return Result.Success;
    }
}
