using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class WaitlistEntryRepository : GenericRepository<WaitlistEntry>, IWaitlistEntryRepository
{
    public WaitlistEntryRepository(DbSet<WaitlistEntry> dbSet)
        : base(dbSet)
    {
    }

    public async Task<WaitlistEntry?> GetByUserAndIdempotencyKeyHashAsync(
        long userId,
        string idempotencyKeyHash,
        CancellationToken ct = default)
    {
        return await _dbSet.FirstOrDefaultAsync(
            x => x.UserId == userId && x.IdempotencyKeyHash == idempotencyKeyHash,
            ct);
    }

    public async Task<WaitlistEntry?> GetByIdAndUserWithClassLockAsync(
        long id,
        long userId,
        CancellationToken ct = default)
    {
        return await _dbSet
            .FromSqlInterpolated($"""
                SELECT w.*
                FROM waitlist_entries AS w
                INNER JOIN classes AS c ON c.id = w.class_id
                WHERE w.id = {id} AND w.user_id = {userId}
                FOR NO KEY UPDATE OF c
                """)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WaitlistQueueState> GetWaitingCountAndUserWaitingStatusAsync(
        long classId,
        long userId,
        CancellationToken ct = default)
    {
        var queueState = await _dbSet
            .Where(entry =>
                entry.ClassId == classId && entry.Status == WaitlistEntryStatuses.Waiting)
            .GroupBy(entry => entry.ClassId)
            .Select(group => new WaitlistQueueState(
                group.Count(),
                group.Any(entry => entry.UserId == userId)))
            .FirstOrDefaultAsync(ct);

        return queueState ?? new WaitlistQueueState(0, false);
    }

    public async Task<List<WaitlistEntry>> GetWaitingEntriesForPromotionAsync(
        long classId,
        int maxSeats,
        long userId,
        int userVisits,
        DateOnly date,
        CancellationToken ct = default)
    {
        return await _dbSet
            .Where(entry =>
                entry.ClassId == classId &&
                entry.Status == WaitlistEntryStatuses.Waiting &&
                entry.Seats <= maxSeats &&
                entry.User.Pass.ValidUntil >= date &&
                (entry.UserId == userId
                    ? entry.User.Pass.RemainingVisits + userVisits >= entry.Seats
                    : entry.User.Pass.RemainingVisits >= entry.Seats))
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(ct);
    }
}
