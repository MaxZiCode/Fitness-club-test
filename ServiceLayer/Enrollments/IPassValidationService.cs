using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public interface IPassValidationService
{
    ErrorOr<Success> ValidateEnrollmentCreate(Pass pass, int requestedSeats, DateOnly date);
    ErrorOr<Success> ValidateEnrollmentUpdate(Pass pass, int difference, DateOnly date);
}
