using DataAccessLayer.Entities;

namespace ServiceLayer.Enrollments.Waitlist;

public sealed record CancelWaitlistResult(long Id, string Status)
{
    public static CancelWaitlistResult FromEntry(WaitlistEntry entry) =>
        new(entry.Id, entry.Status);
}
