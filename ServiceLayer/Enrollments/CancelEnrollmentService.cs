using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using Microsoft.Extensions.Options;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace ServiceLayer.Enrollments;

public sealed class CancelEnrollmentService(
    IUnitOfWork unitOfWork,
    IDateTimeService dateTimeService,
    IPassValidationService passValidationService,
    IEnrollmentCardCacheService enrollmentCardCacheService,
    IIdempotencyKeyHasher idempotencyKeyHasher,
    IOptions<EnrollmentCancellationOptions> options) : ICancelEnrollmentService
{
    private readonly TimeSpan _refundCutoff = options.Value.RefundCutoff;

    public async Task<ErrorOr<CancelEnrollmentResult>> CancelEnrollmentAsync(
        CancelEnrollmentCommand command,
        CancellationToken ct = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var @class = await unitOfWork.Class
            .GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
                command.EnrollmentId,
                command.UserId,
                ct);
        if (@class is null)
            return Error.NotFound();

        var enrollment = await unitOfWork.Enrollment
            .GetByIdAndUserAsync(command.EnrollmentId, command.UserId, ct);
        if (enrollment is null)
            return Error.NotFound();

        if (enrollment.Status == EnrollmentStatuses.Cancelled)
        {
            await transaction.CommitAsync(ct);
            return CancelEnrollmentResult.FromEnrollment(enrollment);
        }

        var now = dateTimeService.GetNow();
        var today = DateOnly.FromDateTime(now);
        if (!@class.IsCancelled && @class.StartsAt <= now)
            return ClassErrors.ClassStarted;

        var refundedVisits = @class.IsCancelled || @class.StartsAt - now >= _refundCutoff
            ? enrollment.Seats
            : 0;

        int freeSeats = 0;
        List<WaitlistEntry> waitingEntries = [];
        if (!@class.IsCancelled)
        {
            var bookedSeats = await unitOfWork.Class.GetBookedSeatsAsync(@class.Id, ct);
            freeSeats = @class.Capacity - (bookedSeats - enrollment.Seats);
            if (freeSeats > 0)
            {
                waitingEntries = await unitOfWork.WaitlistEntry.GetWaitingEntriesForPromotionAsync(
                    @class.Id,
                    freeSeats,
                    command.UserId,
                    enrollment.Seats,
                    today,
                    ct);
            }
        }

        var passLockUserIds = waitingEntries.Select(x => x.UserId).ToHashSet();
        if (refundedVisits > 0)
            passLockUserIds.Add(command.UserId);

        var lockedPasses = await unitOfWork.Pass.GetLockedByUserIdsAsync(
            passLockUserIds,
            ct);

        HashSet<long> updatedUserIds = [];
        if (refundedVisits > 0)
        {
            Pass userPass = lockedPasses.First(x => x.UserId == command.UserId);
            userPass.RemainingVisits += refundedVisits;
            updatedUserIds.Add(userPass.UserId);
        }

        if (freeSeats > 0 && waitingEntries.Count > 0)
        {
            PromoteWaitingEntries(now, today, freeSeats, waitingEntries, lockedPasses, updatedUserIds);
        }

        enrollment.Status = EnrollmentStatuses.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.RefundedVisits = refundedVisits;

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await enrollmentCardCacheService.InvalidateEnrollmentsForUsersAsync(updatedUserIds, ct);

        if (refundedVisits == 0)
        {
            await enrollmentCardCacheService.InvalidateEnrollmentForUserAsync(
                command.UserId,
                enrollment.Id,
                ct);
        }

        return CancelEnrollmentResult.FromEnrollment(enrollment);
    }

    private void PromoteWaitingEntries(DateTime now, DateOnly today, int freeSeats, List<WaitlistEntry> waitingEntries, List<Pass> lockedPasses, HashSet<long> updatedUserIds)
    {
        int remainingSeats = freeSeats;
        var entriesWithPasses = waitingEntries.Join(lockedPasses, e => e.UserId, p => p.UserId,
            (e, p) => (waitingEntry: e, pass: p));
        foreach (var (waitingEntry, pass) in entriesWithPasses)
        {
            if (waitingEntry.Seats <= remainingSeats
                && passValidationService.ValidateEnrollmentCreate(pass, waitingEntry.Seats, today).IsSuccess)
            {
                waitingEntry.Status = WaitlistEntryStatuses.Promoted;
                pass.RemainingVisits -= waitingEntry.Seats;
                updatedUserIds.Add(pass.UserId);
                unitOfWork.Enrollment.Create(CreatePromotedEnrollment(
                    waitingEntry,
                    now));

                remainingSeats -= waitingEntry.Seats;
                if (remainingSeats == 0)
                    break;
            }
        }
    }

    private Enrollment CreatePromotedEnrollment(WaitlistEntry entry, DateTime now)
    {
        string idempotencyKeyHash = idempotencyKeyHasher.Hash($"waitlist-promotion:{entry.Id}");

        return new Enrollment
        {
            UserId = entry.UserId,
            ClassId = entry.ClassId,
            Seats = entry.Seats,
            InitialSeats = entry.Seats,
            Status = EnrollmentStatuses.Active,
            IdempotencyKeyHash = idempotencyKeyHash,
            CreatedAt = now
        };
    }
}
