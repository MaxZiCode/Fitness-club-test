using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface ICreateEnrollmentService
{
    Task<ErrorOr<CreateEnrollmentResult>> CreateEnrollmentAsync(
        CreateEnrollmentCommand command,
        CancellationToken cancellationToken = default);
}
