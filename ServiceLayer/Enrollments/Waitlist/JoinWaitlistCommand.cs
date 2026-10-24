using ServiceLayer.Idempotency;

namespace ServiceLayer.Enrollments.Waitlist;

public sealed record JoinWaitlistCommand(
    long ClassId,
    int Seats,
    string IdempotencyKey,
    long UserId) : IIdempotencyKeyCommand;
