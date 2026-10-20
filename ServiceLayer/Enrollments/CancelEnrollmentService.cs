using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using ServiceLayer.Shared;

namespace ServiceLayer.Enrollments;

public sealed class CancelEnrollmentService(
    IUnitOfWork unitOfWork,
    IDateTimeService dateTimeService,
    IEnrollmentCardCacheService enrollmentCardCacheService) : ICancelEnrollmentService
{
    private static readonly TimeSpan RefundCutoff = TimeSpan.FromHours(2);

    public async Task<ErrorOr<CancelEnrollmentResult>> CancelEnrollmentAsync(
        CancelEnrollmentCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var pass = await unitOfWork.Pass.GetLockedByUserAsync(command.UserId, cancellationToken);
        var classWithEnrollment = await unitOfWork.Class
            .GetLockedClassWithEnrollmentByEnrollmentAndUserIgnoringFiltersAsync(
                command.EnrollmentId,
                command.UserId,
                cancellationToken);
        if (classWithEnrollment is null)
            return Error.NotFound();

        var @class = classWithEnrollment.Class;
        var enrollment = classWithEnrollment.Enrollment;

        if (enrollment.Status == EnrollmentStatuses.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return CancelEnrollmentResult.FromEnrollment(enrollment);
        }

        var now = dateTimeService.GetNow();
        if (!@class.IsCancelled && @class.StartsAt <= now)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassStarted);

        var refundedVisits = @class.IsCancelled || @class.StartsAt - now >= RefundCutoff
            ? enrollment.Seats
            : 0;

        pass.RemainingVisits += refundedVisits;
        enrollment.Status = EnrollmentStatuses.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.RefundedVisits = refundedVisits;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (refundedVisits > 0)
        {
            await enrollmentCardCacheService.InvalidateEnrollmentsForUserAsync(
                command.UserId,
                cancellationToken);
        }
        else
        {
            await enrollmentCardCacheService.InvalidateEnrollmentForUserAsync(
                command.UserId,
                enrollment.Id,
                cancellationToken);
        }

        return CancelEnrollmentResult.FromEnrollment(enrollment);
    }
}
