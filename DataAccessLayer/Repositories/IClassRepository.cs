using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IClassRepository : IGenericRepository<Class>
{
    Task<Class?> GetLockedNoTrackingIgnoringFiltersAsync(
        long id,
        CancellationToken ct = default);

    Task<int> GetBookedSeatsAsync(long classId, CancellationToken ct = default);

    Task<Class?> GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default);

    Task<List<ScheduleClassRow>> GetSchedulePageNoTrackingAsync(
        DateTime from,
        DateTime to,
        long? roomId,
        long userId,
        DateTime? afterStartsAt,
        long? afterClassId,
        int take,
        CancellationToken ct = default);

    Task<List<ClassLoadSummaryRow>> GetLoadSummaryAsync(
        DateTime fromUtc,
        DateTime toUtcExclusive,
        long? roomId,
        CancellationToken ct = default);
    Task<Class?> GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync(long enrollmentId, long userId, CancellationToken ct = default);
}
