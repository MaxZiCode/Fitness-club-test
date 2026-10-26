using ErrorOr;

namespace ServiceLayer.Enrollments.Waitlist;

public static class WaitlistErrors
{
    public static Error SeatsAvailable { get; } = Error.Conflict(
        code: "seats-available",
        description: "Seats are available for the class, so joining the waitlist is not allowed.");

    public static Error AlreadyWaiting { get; } = Error.Conflict(
        code: "already-waiting",
        description: "The user is already on the waitlist for this class.");
}
