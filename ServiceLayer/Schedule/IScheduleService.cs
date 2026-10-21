using ErrorOr;

namespace ServiceLayer.Schedule;

public interface IScheduleService
{
    Task<ErrorOr<SchedulePage>> GetScheduleAsync(ScheduleQuery query, CancellationToken ct = default);
}
