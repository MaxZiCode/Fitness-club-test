using ServiceLayer.Enrollments.Waitlist;

namespace WebApi.Enrollments.Waitlist;

public sealed record JoinWaitlistResponse(
    long Id,
    long ClassId,
    int Seats,
    int Position,
    DateTime CreatedAt)
{
    public static JoinWaitlistResponse FromResult(JoinWaitlistResult result) =>
        new(result.Id, result.ClassId, result.Seats, result.Position, result.CreatedAt);
}
