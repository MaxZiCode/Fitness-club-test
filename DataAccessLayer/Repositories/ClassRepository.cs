using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class ClassRepository : GenericRepository<Class>, IClassRepository
{
    public ClassRepository(DbSet<Class> dbSet)
        : base(dbSet)
    {
    }

    public async Task<Class?> GetLockedNoTrackingIgnoringFiltersWithEnrollmentsAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .FromSqlInterpolated($"SELECT * FROM classes WHERE id = {id} FOR NO KEY UPDATE")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Enrollments)
            .FirstOrDefaultAsync(ct);
    }
}
