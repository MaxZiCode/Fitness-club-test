using ErrorOr;

namespace ServiceLayer.Enrollments.Waitlist;

public interface ICancelWaitlistService
{
    Task<ErrorOr<CancelWaitlistResult>> CancelWaitlistAsync(
        CancelWaitlistCommand command,
        CancellationToken cancellationToken = default);
}
