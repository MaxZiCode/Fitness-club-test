using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IPassRepository : IGenericRepository<Pass>
{
    Task<Pass> GetByUserNoTrackingAsync(long userId, CancellationToken ct = default);

    Task<Pass> GetLockedByUserAsync(long userId, CancellationToken ct = default);
}
