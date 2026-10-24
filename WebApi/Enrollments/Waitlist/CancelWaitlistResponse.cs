using ServiceLayer.Enrollments.Waitlist;

namespace WebApi.Enrollments.Waitlist;

public sealed record CancelWaitlistResponse(long Id, string Status)
{
    public static CancelWaitlistResponse FromResult(CancelWaitlistResult result) =>
        new(result.Id, result.Status);
}
