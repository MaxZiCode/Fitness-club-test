using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IClassRepository : IGenericRepository<Class>
{
    Task<ClassWithBookedSeats?> GetLockedNoTrackingIgnoringFiltersWithBookedSeatsAsync(
        long id,
        CancellationToken ct = default);

    Task<ClassWithEnrollmentAndBookedSeats?> GetLockedClassWithEnrollmentAndBookedSeatsByEnrollmentAndUserIgnoringFiltersAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default);

    Task<ClassWithEnrollment?> GetLockedClassWithEnrollmentByEnrollmentAndUserIgnoringFiltersAsync(
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
}
