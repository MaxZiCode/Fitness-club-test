using ErrorOr;

namespace ServiceLayer.Idempotency;

public static class IdempotencyErrors
{
    public static Error PayloadMismatch { get; } = Error.Conflict(
        code: "idempotency-payload-mismatch",
        description: "The idempotency key was already used with a different request payload.");
}
