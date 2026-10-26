using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories;

public class EnrollmentRepository : GenericRepository<Enrollment>, IEnrollmentRepository
{
    public EnrollmentRepository(DbSet<Enrollment> dbSet)
        : base(dbSet)
    {
    }

    public async Task<List<Enrollment>> GetByUserAsync(long userId, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<Enrollment?> GetByIdAndUserWithClassAndRoomNoTrackingAsync(
        long id,
        long userId,
        CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(x => x.Class)
            .ThenInclude(x => x.Room)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
    }

    public async Task<Enrollment?> GetByIdAndUserAsync(
        long id,
        long userId,
        CancellationToken ct = default)
    {
        return await _dbSet
            .Where(enrollment => enrollment.Id == id && enrollment.UserId == userId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Enrollment?> GetByUserAndIdempotencyKeyHashAsync(
        long userId,
        string idempotencyKeyHash,
        CancellationToken ct = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.IdempotencyKeyHash == idempotencyKeyHash,
                ct);
    }
}
