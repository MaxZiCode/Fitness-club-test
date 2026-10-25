using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace ServiceLayer.Enrollments;

public sealed class CreateEnrollmentService(
    IUnitOfWork unitOfWork,
    IDateTimeService dateTimeService,
    IIdempotencyKeyHasher idempotencyKeyHasher,
    IPassValidationService passValidationService,
    IClassValidationService classValidationService,
    IEnrollmentCardCacheService enrollmentCardCacheService) : ICreateEnrollmentService
{
    public async Task<ErrorOr<CreateEnrollmentResult>> CreateEnrollmentAsync(
        CreateEnrollmentCommand command,
        CancellationToken ct = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var @class = await unitOfWork.Class.GetLockedNoTrackingIgnoringFiltersAsync(
            command.ClassId,
            ct);
        var pass = await unitOfWork.Pass.GetLockedByUserIdAsync(command.UserId, ct);

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

        if (@class is null)
            return Error.NotFound();

        var now = dateTimeService.GetNow();
        var passValidationResult = passValidationService.Validate(
            pass,
            command.Seats,
            DateOnly.FromDateTime(now));
        if (passValidationResult.IsError)
            return passValidationResult.Errors;

        var bookedSeats = await unitOfWork.Class.GetBookedSeatsAsync(command.ClassId, ct);
        var classResult = classValidationService.ValidateEnrollment(
            @class,
            bookedSeats,
            command.Seats,
            now);
        if (classResult.IsError)
            return classResult.Errors;

        ConsumePassVisits(pass, command.Seats);

        var enrollment = CreateEnrollment(command, keyHash, now);
        unitOfWork.Enrollment.Create(enrollment);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await enrollmentCardCacheService.InvalidateEnrollmentsForUserAsync(command.UserId, ct);

        return CreateEnrollmentResult.FromEnrollment(enrollment);
    }

    private static void ConsumePassVisits(Pass pass, int seats)
    {
        pass.RemainingVisits -= seats;
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
            InitialSeats = command.Seats,
            Status = EnrollmentStatuses.Active,
            IdempotencyKeyHash = idempotencyKeyHash,
            CreatedAt = now,
        };
    }

    private static ErrorOr<CreateEnrollmentResult> ReplayOrReturnPayloadMismatch(
        Enrollment existing,
        CreateEnrollmentCommand command)
    {
        if (existing.ClassId != command.ClassId || existing.InitialSeats != command.Seats)
            return IdempotencyErrors.PayloadMismatch;

        return CreateEnrollmentResult.FromEnrollment(existing);
    }
}
