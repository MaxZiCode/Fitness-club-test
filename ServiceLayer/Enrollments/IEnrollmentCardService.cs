using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface IEnrollmentCardService
{
    Task<ErrorOr<EnrollmentCard>> GetEnrollmentCardAsync(
        GetEnrollmentCardCommand command,
        CancellationToken cancellationToken = default);
}
