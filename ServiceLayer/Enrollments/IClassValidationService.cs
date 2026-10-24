using ErrorOr;
namespace ServiceLayer.Enrollments;

public interface IClassValidationService
{
    ErrorOr<Success> ValidateEnrollment(
        DataAccessLayer.Entities.Class @class,
        int bookedSeats,
        int seatsDiff,
        DateTime now);

    ErrorOr<Success> ValidateWaitlist(
        DataAccessLayer.Entities.Class @class,
        int bookedSeats,
        int requestedSeats,
        DateTime now);
}
