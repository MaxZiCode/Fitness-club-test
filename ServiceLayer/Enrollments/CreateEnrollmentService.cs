using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace ServiceLayer.Enrollments;

public sealed class CreateEnrollmentService(
    IUnitOfWork unitOfWork,
    IDateTimeService dateTimeService,
    IIdempotencyKeyHasher idempotencyKeyHasher) : ICreateEnrollmentService
{
    public async Task<ErrorOr<Enrollment>> CreateEnrollmentAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var pass = await unitOfWork.Pass.GetLockedByUserAsync(command.UserId, ct)
            ?? throw new InvalidOperationException($"No pass found for user {command.UserId}.");

        var keyHash = idempotencyKeyHasher.Hash(command.IdempotencyKey);
        var existing = await unitOfWork.Enrollment.GetByUserAndIdempotencyKeyHashAsync(
            command.UserId,
            keyHash,
            ct);
        if (existing is not null)
        {
            await transaction.CommitAsync(ct);
            return ReplayOrReturnPayloadMismatch(existing, command);
        }

        var now = dateTimeService.GetNow();
        var passError = GetPassValidationError(pass, command.Seats, now);
        if (passError is not null)
            return passError.Value;

        var classResult = await GetBookableClassAsync(command.ClassId, command.Seats, now, ct);
        if (classResult.IsError)
            return classResult.Errors;

        ConsumePassVisits(pass, command.Seats);

        var enrollment = CreateEnrollment(command, keyHash, now);
        await unitOfWork.Enrollment.CreateAsync(enrollment, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return enrollment;
    }

    private static void ConsumePassVisits(Pass pass, int seats)
    {
        pass.RemainingVisits -= seats;
    }

    private static Error? GetPassValidationError(Pass pass, int requestedSeats, DateTime now)
    {
        if (pass.ValidUntil <= DateOnly.FromDateTime(now))
            return Error.Conflict(EnrollmentErrors.Conflict.PassExpired);

        if (pass.RemainingVisits < requestedSeats)
            return Error.Conflict(EnrollmentErrors.Conflict.PassExhausted);

        return null;
    }

    private async Task<ErrorOr<Class>> GetBookableClassAsync(
        long classId,
        int requestedSeats,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var @class = await unitOfWork.Class
            .GetLockedNoTrackingIgnoringFiltersWithEnrollmentsAsync(classId, cancellationToken);
        if (@class is null)
            return Error.NotFound();

        if (@class.IsCancelled)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassCancelled);

        if (@class.StartsAt <= now)
            return Error.Conflict(EnrollmentErrors.Conflict.ClassStarted);

        var bookedSeats = @class.Enrollments.Sum(enrollment => enrollment.Seats);
        if (@class.Capacity < bookedSeats + requestedSeats)
            return Error.Conflict(EnrollmentErrors.Conflict.CapacityExceeded);

        return @class;
    }

    private static Enrollment CreateEnrollment(
        CreateEnrollmentCommand command,
        string idempotencyKeyHash,
        DateTime now)
    {
        return new Enrollment
        {
            UserId = command.UserId,
            ClassId = command.ClassId,
            Seats = command.Seats,
            Status = EnrollmentStatuses.Active,
            IdempotencyKeyHash = idempotencyKeyHash,
            CreatedAt = now,
        };
    }

    private static ErrorOr<Enrollment> ReplayOrReturnPayloadMismatch(
        Enrollment existing,
        CreateEnrollmentCommand command)
    {
        if (existing.ClassId != command.ClassId || existing.Seats != command.Seats)
            return IdempotencyErrors.PayloadMismatch;

        return existing;
    }
}
