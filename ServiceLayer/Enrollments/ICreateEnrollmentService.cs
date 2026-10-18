using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface ICreateEnrollmentService
{
    Task<ErrorOr<Enrollment>> CreateEnrollmentAsync(
        CreateEnrollmentCommand command,
        CancellationToken cancellationToken = default);
}
