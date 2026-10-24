using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface IPassValidationService
{
    ErrorOr<Success> Validate(Pass pass, int requestedSeats, DateOnly date);
}
