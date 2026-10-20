using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface ICancelEnrollmentService
{
    Task<ErrorOr<CancelEnrollmentResult>> CancelEnrollmentAsync(
        CancelEnrollmentCommand command,
        CancellationToken cancellationToken = default);
}
