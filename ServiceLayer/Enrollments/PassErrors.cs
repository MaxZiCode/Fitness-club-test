using ErrorOr;

namespace ServiceLayer.Enrollments;

public static class PassErrors
{
    public static readonly Error PassExhausted = Error.Conflict(
        code: "pass-exhausted",
        description: "The pass does not have enough remaining visits.");

    public static readonly Error PassExpired = Error.Conflict(
        code: "pass-expired",
        description: "The pass has expired.");
}
