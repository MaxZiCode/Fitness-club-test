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
            .Include(x => x.Room)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Class?> GetLockedNoTrackingIgnoringFiltersByEnrollmentAndUserWithEnrollmentsAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default)
    {
        return await _dbSet
            .FromSqlInterpolated($"""
                SELECT c.*
                FROM classes AS c
                WHERE EXISTS (
                    SELECT 1
                    FROM enrollments AS e
                    WHERE e.class_id = c.id
                        AND e.id = {enrollmentId}
                        AND e.user_id = {userId})
                FOR NO KEY UPDATE OF c
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Enrollments)
            .Include(x => x.Room)
            .FirstOrDefaultAsync(ct);
    }
}
