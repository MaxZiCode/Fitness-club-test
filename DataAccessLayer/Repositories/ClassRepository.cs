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

    public async Task<ClassWithBookedSeats?> GetLockedNoTrackingIgnoringFiltersWithBookedSeatsAsync(
        long id,
        CancellationToken ct = default)
    {
        return await _dbSet
            .FromSqlInterpolated($"SELECT * FROM classes WHERE id = {id} FOR NO KEY UPDATE")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Room)
            .Select(@class => new ClassWithBookedSeats(
                @class,
                @class.Enrollments
                    .Where(enrollment => enrollment.Status == EnrollmentStatuses.Active)
                    .Sum(enrollment => enrollment.Seats)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ClassWithEnrollmentAndBookedSeats?> GetLockedClassWithEnrollmentAndBookedSeatsByEnrollmentAndUserIgnoringFiltersAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default)
    {
        return await GetLockedClassByEnrollmentAndUserIgnoringFilters(enrollmentId, userId)
            .Select(@class => new ClassWithEnrollmentAndBookedSeats(
                @class,
                @class.Enrollments
                    .Where(enrollment => enrollment.Status == EnrollmentStatuses.Active)
                    .Sum(enrollment => enrollment.Seats),
                @class.Enrollments.First(enrollment =>
                    enrollment.Id == enrollmentId && enrollment.UserId == userId)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ClassWithEnrollment?> GetLockedClassWithEnrollmentByEnrollmentAndUserIgnoringFiltersAsync(
        long enrollmentId,
        long userId,
        CancellationToken ct = default)
    {
        return await GetLockedClassByEnrollmentAndUserIgnoringFilters(enrollmentId, userId)
            .Select(@class => new ClassWithEnrollment(
                @class,
                @class.Enrollments.First(enrollment =>
                    enrollment.Id == enrollmentId && enrollment.UserId == userId)))
            .FirstOrDefaultAsync(ct);
    }

    private IQueryable<Class> GetLockedClassByEnrollmentAndUserIgnoringFilters(long enrollmentId, long userId)
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
            .IgnoreQueryFilters()
            .Include(x => x.Room);
    }
}
