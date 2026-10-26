using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;

namespace ServiceLayer.Enrollments.Waitlist;

public sealed class CancelWaitlistService(IUnitOfWork unitOfWork) : ICancelWaitlistService
{
    public async Task<ErrorOr<CancelWaitlistResult>> CancelWaitlistAsync(
        CancelWaitlistCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var entry = await unitOfWork.WaitlistEntry.GetByIdAndUserWithClassLockAsync(
            command.Id,
            command.UserId,
            cancellationToken);
        if (entry is null)
            return Error.NotFound();

        if (entry.Status == WaitlistEntryStatuses.Waiting)
        {
            entry.Status = WaitlistEntryStatuses.Cancelled;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CancelWaitlistResult.FromEntry(entry);
    }
}
