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
        var classValidationResult = classValidationService.ValidateEnrollment(
            @class,
            classWithEnrollmentAndBookedSeats.BookedSeats,
            difference,
            now);
        if (classValidationResult.IsError)
            return classValidationResult.Errors;

        var enrollmentValidationResult = enrollmentValidationService.Validate(enrollment);
        if (enrollmentValidationResult.IsError)
            return enrollmentValidationResult.Errors;

        var pass = await unitOfWork.Pass.GetLockedByUserIdAsync(command.UserId, ct);
        if (difference == 0)
        {
            await transaction.CommitAsync(ct);
            return EnrollmentCard.FromEnrollment(enrollment, pass);
        }

        if (difference > 0)
        {
            enrollmentValidationResult = passValidationService.Validate(
                pass,
                difference,
                DateOnly.FromDateTime(now));
            if (enrollmentValidationResult.IsError)
                return enrollmentValidationResult.Errors;
        }

        pass.RemainingVisits -= difference;

        enrollment.Seats = command.Seats;
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await enrollmentCardCacheService.InvalidateEnrollmentsForUserAsync(command.UserId, ct);

        return EnrollmentCard.FromEnrollment(enrollment, pass);
    }
}
