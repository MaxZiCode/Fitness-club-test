using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface IUpdateEnrollmentSeatsService
{
    Task<ErrorOr<EnrollmentCard>> UpdateSeatsAsync(
        UpdateEnrollmentSeatsCommand command,
        CancellationToken cancellationToken = default);
}
