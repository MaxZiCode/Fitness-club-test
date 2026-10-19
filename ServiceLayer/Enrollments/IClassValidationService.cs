using ErrorOr;
namespace ServiceLayer.Enrollments;

public interface IClassValidationService
{
    ErrorOr<Success> Validate(
        DataAccessLayer.Entities.Class @class,
        int seatsDiff,
        DateTime now);
}
