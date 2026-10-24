using DataAccessLayer.Entities;

namespace ServiceLayer.Enrollments.Waitlist;

public sealed record JoinWaitlistResult(
    long Id,
    long ClassId,
    int Seats,
    int Position,
    DateTime CreatedAt)
{
    public static JoinWaitlistResult FromEntry(WaitlistEntry entry) =>
        new(entry.Id, entry.ClassId, entry.Seats, entry.InitialPosition, entry.CreatedAt);
}
