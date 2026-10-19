using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IClassRepository : IGenericRepository<Class>
{
    Task<Class?> GetLockedNoTrackingIgnoringFiltersWithEnrollmentsAsync(long id, CancellationToken ct = default);

    Task<Class?> GetLockedNoTrackingIgnoringFiltersByEnrollmentAndUserWithEnrollmentsAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default);
}
