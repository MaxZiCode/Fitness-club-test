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
        var @class = await unitOfWork.Class
            .GetLockedNoTrackingIgnoringFiltersByEnrollmentAndUserWithEnrollmentsAsync(
                command.EnrollmentId,
                command.UserId,
                ct);
        if (@class is null)
            return Error.NotFound();

        var enrollment = @class.Enrollments.First(x =>
            x.Id == command.EnrollmentId && x.UserId == command.UserId);
        enrollment.Class = @class;

        var now = dateTimeService.GetNow();
        var difference = command.Seats - enrollment.Seats;
        var classValidationResult = classValidationService.Validate(@class, difference, now);
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
            unitOfWork.Enrollment.UpdateSeats(enrollment.Id, command.Seats);
            await unitOfWork.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        if (difference != 0)
            await enrollmentCardCacheService.InvalidateEnrollmentsForUserAsync(command.UserId, ct);

        return EnrollmentCard.FromEnrollment(enrollment, pass);
    }
}
