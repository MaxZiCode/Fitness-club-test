using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using ServiceLayer.Enrollments;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace ServiceLayer.Enrollments.Waitlist;

public sealed class JoinWaitlistService(
    IUnitOfWork unitOfWork,
    IDateTimeService dateTimeService,
    IIdempotencyKeyHasher idempotencyKeyHasher,
    IClassValidationService classValidationService) : IJoinWaitlistService
{
    public async Task<ErrorOr<JoinWaitlistResult>> JoinWaitlistAsync(
        JoinWaitlistCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var classWithBookedSeats = await unitOfWork.Class
            .GetLockedNoTrackingIgnoringFiltersWithBookedSeatsAsync(command.ClassId, cancellationToken);

        var keyHash = idempotencyKeyHasher.Hash(command.IdempotencyKey);
        var existing = await unitOfWork.WaitlistEntry.GetByUserAndIdempotencyKeyHashAsync(
            command.UserId,
            keyHash,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReplayOrReturnPayloadMismatch(existing, command);
        }

        if (classWithBookedSeats is null)
            return Error.NotFound();

        var @class = classWithBookedSeats.Class;
        var now = dateTimeService.GetNow();
        var classValidationResult = classValidationService.ValidateWaitlist(
            @class,
            classWithBookedSeats.BookedSeats,
            command.Seats,
            now);
        if (classValidationResult.IsError)
            return classValidationResult.Errors;

        var queueState = await unitOfWork.WaitlistEntry.GetWaitingCountAndUserWaitingStatusAsync(
            command.ClassId,
            command.UserId,
            cancellationToken);
        if (queueState.HasWaitingForUser)
            return Error.Conflict(WaitlistErrors.Conflict.AlreadyWaiting);

        var position = queueState.WaitingCount + 1;
        WaitlistEntry entry = CreateWaitlistEntry(command, keyHash, now, position);

        unitOfWork.WaitlistEntry.Create(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return JoinWaitlistResult.FromEntry(entry);
    }

    private static WaitlistEntry CreateWaitlistEntry(
        JoinWaitlistCommand command,
        string keyHash,
        DateTime createdAt,
        int position)
    {
        return new WaitlistEntry
        {
            UserId = command.UserId,
            ClassId = command.ClassId,
            Seats = command.Seats,
            InitialPosition = position,
            Status = WaitlistEntryStatuses.Waiting,
            IdempotencyKeyHash = keyHash,
            CreatedAt = createdAt,
        };
    }

    private static ErrorOr<JoinWaitlistResult> ReplayOrReturnPayloadMismatch(
        WaitlistEntry existing,
        JoinWaitlistCommand command)
    {
        if (existing.ClassId != command.ClassId || existing.Seats != command.Seats)
            return IdempotencyErrors.PayloadMismatch;

        return JoinWaitlistResult.FromEntry(existing);
    }
}
