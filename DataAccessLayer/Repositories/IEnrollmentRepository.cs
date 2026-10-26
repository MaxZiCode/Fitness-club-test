using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IEnrollmentRepository : IGenericRepository<Enrollment>
{
    Task<List<Enrollment>> GetByUserAsync(long userId, CancellationToken ct = default);

    Task<Enrollment?> GetByIdAndUserWithClassAndRoomNoTrackingAsync(
        long id,
        long userId,
        CancellationToken ct = default);

    Task<Enrollment?> GetByIdAndUserAsync(
        long id,
        long userId,
        CancellationToken ct = default);

    Task<Enrollment?> GetByUserAndIdempotencyKeyHashAsync(
        long userId,
        string idempotencyKeyHash,
        CancellationToken ct = default);
}
