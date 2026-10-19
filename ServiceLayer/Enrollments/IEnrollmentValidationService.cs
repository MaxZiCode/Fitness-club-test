using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface IEnrollmentValidationService
{
    ErrorOr<Success> Validate(Enrollment enrollment);
}
