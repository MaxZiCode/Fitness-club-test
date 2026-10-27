using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;

namespace ServiceLayer.Enrollments.Waitlist;

public sealed class CancelWaitlistService(IUnitOfWork unitOfWork) : ICancelWaitlistService
{
    public async Task<ErrorOr<CancelWaitlistResult>> CancelWaitlistAsync(
        CancelWaitlistCommand command,
        CancellationToken ct = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var @class = await unitOfWork.Class.GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
            command.Id,
            command.UserId,
            ct);
        if (@class is null)
            return Error.NotFound();

        var entry = await unitOfWork.WaitlistEntry.GetByIdAndUserAsync(
            command.Id,
            command.UserId,
            ct);
        if (entry is null || entry.Status == WaitlistEntryStatuses.Promoted)
            return Error.NotFound();

        if (entry.Status == WaitlistEntryStatuses.Waiting)
        {
            entry.Status = WaitlistEntryStatuses.Cancelled;
            await unitOfWork.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return CancelWaitlistResult.FromEntry(entry);
    }
}
