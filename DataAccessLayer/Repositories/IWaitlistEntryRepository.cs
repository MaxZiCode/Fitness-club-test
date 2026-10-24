using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public interface IWaitlistEntryRepository : IGenericRepository<WaitlistEntry>
{
    Task<WaitlistEntry?> GetByUserAndIdempotencyKeyHashAsync(
        long userId,
        string idempotencyKeyHash,
        CancellationToken ct = default);

    Task<WaitlistEntry?> GetByIdAndUserWithClassLockAsync(
        long id,
        long userId,
        CancellationToken ct = default);

    Task<WaitlistQueueState> GetWaitingCountAndUserWaitingStatusAsync(
        long classId,
        long userId,
        CancellationToken ct = default);

    Task<List<WaitlistEntry>> GetWaitingEntriesForPromotionAsync(
        long classId,
        int maxSeats,
        long userId,
        int userVisits,
        DateOnly date,
        CancellationToken ct = default);
}
