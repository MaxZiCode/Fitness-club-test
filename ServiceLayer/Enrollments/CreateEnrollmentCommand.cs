using ServiceLayer.Idempotency;

namespace ServiceLayer.Enrollments;

public sealed record CreateEnrollmentCommand(
    long ClassId,
    int Seats,
    string IdempotencyKey,
    long UserId) : IIdempotencyKeyCommand;
