using DataAccessLayer.UnitOfWork;
using ErrorOr;

namespace ServiceLayer.Schedule;

public sealed class ScheduleService(
    IUnitOfWork unitOfWork,
    IScheduleContinuationTokenService continuationTokenService) : IScheduleService
{
    public async Task<ErrorOr<SchedulePage>> GetScheduleAsync(ScheduleQuery query, CancellationToken ct = default)
    {
        ScheduleCursor? cursor = null;
        if (query.ContinuationToken is not null)
        {
            var cursorResult = continuationTokenService.Decode(query.ContinuationToken);
            if (cursorResult.IsError)
                return cursorResult.Errors;

            cursor = cursorResult.Value;
        }

        var rows = await unitOfWork.Class.GetSchedulePageNoTrackingAsync(
            query.From, query.To, query.RoomId, query.UserId,
            cursor?.StartsAt, cursor?.ClassId, query.Limit + 1, ct);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit).Select(ScheduleItem.FromRow).ToArray();

        string? nextToken = null;
        if (hasMore)
        {
            var last = items[^1];
            nextToken = continuationTokenService.Encode(new ScheduleCursor(last.StartsAt, last.ClassId));
        }

        return new SchedulePage(items, nextToken);
    }
}
