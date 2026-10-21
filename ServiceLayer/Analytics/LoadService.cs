using DataAccessLayer.UnitOfWork;
using ErrorOr;

namespace ServiceLayer.Analytics;

public sealed class LoadService(IUnitOfWork unitOfWork) : ILoadService
{
    public async Task<ErrorOr<LoadReport>> GetLoadAsync(LoadQuery query, CancellationToken cancellationToken = default)
    {
        var fromUtc = query.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = query.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await unitOfWork.Class.GetLoadSummaryAsync(
            fromUtc, toUtcExclusive, query.RoomId, cancellationToken);

        var items = rows
            .Select(LoadItem.FromClassLoadSummaryRow)
            .ToArray();

        return new LoadReport(items);
    }
}
