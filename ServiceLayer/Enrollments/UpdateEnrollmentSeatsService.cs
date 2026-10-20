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

        var pass = await unitOfWork.Pass.GetLockedByUserAsync(command.UserId, ct);
        var classWithEnrollmentAndBookedSeats = await unitOfWork.Class
            .GetLockedClassWithEnrollmentAndBookedSeatsByEnrollmentAndUserIgnoringFiltersAsync(
                command.EnrollmentId,
                command.UserId,
                ct);
        if (classWithEnrollmentAndBookedSeats is null)
            return Error.NotFound();

        var @class = classWithEnrollmentAndBookedSeats.Class;
        var enrollment = classWithEnrollmentAndBookedSeats.Enrollment;
        enrollment.Class = @class;

        var now = dateTimeService.GetNow();
        var difference = command.Seats - enrollment.Seats;
        var classValidationResult = classValidationService.Validate(
            @class,
            classWithEnrollmentAndBookedSeats.BookedSeats,
            difference,
            now);
        if (classValidationResult.IsError)
            return classValidationResult.Errors;

        var enrollmentValidationResult = enrollmentValidationService.Validate(enrollment);
        if (enrollmentValidationResult.IsError)
            return enrollmentValidationResult.Errors;

        if (difference > 0)
        {
            enrollmentValidationResult = passValidationService.Validate(pass, difference, now);
            if (enrollmentValidationResult.IsError)
                return enrollmentValidationResult.Errors;
        }


        if (difference != 0)
        {
            pass.RemainingVisits -= difference;

            enrollment.Seats = command.Seats;
            await unitOfWork.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        if (difference != 0)
            await enrollmentCardCacheService.InvalidateEnrollmentsForUserAsync(command.UserId, ct);

        return EnrollmentCard.FromEnrollment(enrollment, pass);
    }
}
