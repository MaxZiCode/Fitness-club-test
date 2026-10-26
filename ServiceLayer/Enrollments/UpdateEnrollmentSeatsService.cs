using DataAccessLayer.UnitOfWork;
using ErrorOr;
using ServiceLayer.Shared;

namespace ServiceLayer.Enrollments;

public sealed class UpdateEnrollmentSeatsService(
    IUnitOfWork unitOfWork,
    IDateTimeService dateTimeService,
    IPassValidationService passValidationService,
    IClassValidationService classValidationService,
    IEnrollmentValidationService enrollmentValidationService,
    IEnrollmentCardCacheService enrollmentCardCacheService) : IUpdateEnrollmentSeatsService
{
    public async Task<ErrorOr<EnrollmentCard>> UpdateSeatsAsync(
        UpdateEnrollmentSeatsCommand command,
        CancellationToken ct = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var @class = await unitOfWork.Class
            .GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync(
                command.EnrollmentId,
                command.UserId,
                ct);
        if (@class is null)
            return Error.NotFound();

        var enrollment = await unitOfWork.Enrollment
            .GetByIdAndUserAsync(command.EnrollmentId, command.UserId, ct);
        if (enrollment is null)
            return Error.NotFound();

        enrollment.Class = @class;

        var now = dateTimeService.GetNow();
        var difference = command.Seats - enrollment.Seats;
        var bookedSeats = await unitOfWork.Class.GetBookedSeatsAsync(@class.Id, ct);
        var classValidationResult = classValidationService.ValidateEnrollment(
            @class,
            bookedSeats,
            difference,
            now);
        if (classValidationResult.IsError)
            return classValidationResult.Errors;

        var enrollmentValidationResult = enrollmentValidationService.Validate(enrollment);
        if (enrollmentValidationResult.IsError)
            return enrollmentValidationResult.Errors;

        var pass = await unitOfWork.Pass.GetLockedByUserIdAsync(command.UserId, ct);
        enrollmentValidationResult = passValidationService.ValidateEnrollmentUpdate(
            pass,
            difference,
            DateOnly.FromDateTime(now));
        if (enrollmentValidationResult.IsError)
            return enrollmentValidationResult.Errors;

        if (difference == 0)
        {
            await transaction.CommitAsync(ct);
            return EnrollmentCard.FromEnrollment(enrollment, pass);
        }

        pass.RemainingVisits -= difference;

        // решение: не продвигаем waitlist потому что этого не было в задании.

        enrollment.Seats = command.Seats;
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await enrollmentCardCacheService.InvalidateEnrollmentsForUserAsync(command.UserId, ct);

        return EnrollmentCard.FromEnrollment(enrollment, pass);
    }
}
