using ErrorOr;

namespace ServiceLayer.Enrollments.Waitlist;

public interface IJoinWaitlistService
{
    Task<ErrorOr<JoinWaitlistResult>> JoinWaitlistAsync(
        JoinWaitlistCommand command,
        CancellationToken cancellationToken = default);
}
