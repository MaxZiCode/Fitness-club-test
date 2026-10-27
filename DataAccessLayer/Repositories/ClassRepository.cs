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

    public async Task<List<ScheduleClassRow>> GetSchedulePageNoTrackingAsync(
        DateTime from,
        DateTime toExclusive,
        long? roomId,
        long userId,
        DateTime? afterStartsAt,
        long? afterClassId,
        int take,
        CancellationToken ct = default)
    {
        var query = _dbSet.AsNoTracking()
            .Where(c => c.StartsAt >= from && c.StartsAt < toExclusive);
        if (roomId.HasValue)
            query = query.Where(c => c.RoomId == roomId.Value);
        if (afterStartsAt.HasValue && afterClassId.HasValue)
            query = query.Where(c => c.StartsAt > afterStartsAt.Value ||
                (c.StartsAt == afterStartsAt.Value && c.Id > afterClassId.Value));

        return await query
            .OrderBy(c => c.StartsAt)
            .ThenBy(c => c.Id)
            .Select(c => new ScheduleClassRow(
                c.Id,
                c.Title,
                c.StartsAt,
                c.DurationMinutes,
                c.RoomId,
                c.Room.Name,
                c.Capacity,
                c.Enrollments.Where(e => e.Status == EnrollmentStatuses.Active).Sum(e => e.Seats),
                c.Enrollments.Where(e => e.Status == EnrollmentStatuses.Active && e.UserId == userId)
                    .Sum(e => e.Seats)))
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<List<ClassLoadSummaryRow>> GetLoadSummaryAsync(
        DateTime from,
        DateTime toExclusive,
        long? roomId,
        CancellationToken ct = default)
    {
        var classes = _dbSet.AsNoTracking()
            .Where(@class => @class.StartsAt >= from && @class.StartsAt < toExclusive);

        if (roomId.HasValue)
            classes = classes.Where(@class => @class.RoomId == roomId.Value);

        return await classes
            .GroupBy(@class => new
            {
                @class.StartsAt.Date,
                @class.RoomId,
                RoomName = @class.Room.Name
            })
            .OrderBy(group => group.Key.Date)
            .ThenBy(group => group.Key.RoomId)
            .Select(group => new ClassLoadSummaryRow(
                group.Key.Date,
                group.Key.RoomId,
                group.Key.RoomName,
                group.Count(),
                group.Sum(@class => @class.Capacity),
                group.Sum(@class => @class.Enrollments
                    .Where(enrollment => enrollment.Status == EnrollmentStatuses.Active)
                    .Sum(enrollment => enrollment.Seats))))
            .ToListAsync(ct);
    }

    public async Task<Class?> GetLockedNoTrackingIgnoringFiltersAsync(
        long id,
        CancellationToken ct = default)
    {
        return await _dbSet
            .FromSqlInterpolated($"SELECT * FROM classes WHERE id = {id} FOR NO KEY UPDATE")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> GetBookedSeatsAsync(long classId, CancellationToken ct = default)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .Where(@class => @class.Id == classId)
            .Select(@class => @class.Enrollments
                .Where(enrollment => enrollment.Status == EnrollmentStatuses.Active)
                .Sum(enrollment => enrollment.Seats))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Class?> GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default)
    {
        return await GetLockedByEnrollmentAndUserIgnoringFiltersQuery(enrollmentId, userId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Class?> GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default)
    {
        return await GetLockedByEnrollmentAndUserIgnoringFiltersQuery(enrollmentId, userId)
            .Include(x => x.Room)
            .FirstOrDefaultAsync(ct);
    }

    private IQueryable<Class> GetLockedByEnrollmentAndUserIgnoringFiltersQuery(long enrollmentId, long userId)
    {
        return _dbSet
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
            .AsNoTracking()
            .IgnoreQueryFilters();
    }

    public Task<Class?> GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
        long waitlistEntryId,
        long userId,
        CancellationToken ct = default)
    {
        return _dbSet
            .FromSqlInterpolated($"""
                SELECT c.*
                FROM classes AS c
                WHERE EXISTS (
                    SELECT 1
                    FROM waitlist_entries AS e
                    WHERE e.class_id = c.id
                        AND e.id = {waitlistEntryId}
                        AND e.user_id = {userId})
                FOR NO KEY UPDATE OF c
                """)
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);
    }
}
