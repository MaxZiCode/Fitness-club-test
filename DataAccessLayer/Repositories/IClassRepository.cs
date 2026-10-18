using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IClassRepository : IGenericRepository<Class>
{
    Task<Class?> GetLockedNoTrackingIgnoringFiltersWithEnrollmentsAsync(long id, CancellationToken ct = default);
}
