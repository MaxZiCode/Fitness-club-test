using ErrorOr;

namespace ServiceLayer.Schedule;

public static class ScheduleErrors
{
    public static Error InvalidContinuationToken { get; } = Error.Validation(
        code: "schedule-invalid-continuation-token",
        description: "Continuation token is invalid.");
}
