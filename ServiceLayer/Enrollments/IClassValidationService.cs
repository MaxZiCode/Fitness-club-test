using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface IClassValidationService
{
    Task<ErrorOr<Success>> ValidateAsync(
        long classId,
        int requestedSeats,
        DateTime now,
        CancellationToken cancellationToken = default);
}
