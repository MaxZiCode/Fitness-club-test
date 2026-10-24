using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class PassRepository : GenericRepository<Pass>, IPassRepository
{
    public PassRepository(DbSet<Pass> dbSet)
        : base(dbSet)
    {
    }

    public Task<Pass> GetByUserNoTrackingAsync(long userId, CancellationToken ct = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        return GetRequiredPassAsync(query, userId, ct);
    }

    public Task<Pass> GetLockedByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var query = _dbSet
            .FromSqlInterpolated($"SELECT * FROM passes WHERE user_id = {userId} FOR NO KEY UPDATE");

        return GetRequiredPassAsync(query, userId, ct);
    }

    public Task<List<Pass>> GetLockedByUserIdsAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken ct = default)
    {
        var query = _dbSet.FromSqlInterpolated($"""
            SELECT p.*
            FROM passes AS p
            WHERE p.user_id = ANY({userIds})
            ORDER BY p.id
            FOR NO KEY UPDATE
            """);

        return query.ToListAsync(ct);
    }

    private static async Task<Pass> GetRequiredPassAsync(
        IQueryable<Pass> query,
        long userId,
        CancellationToken ct)
    {
        return await query.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No pass found for user {userId}.");
    }
}
