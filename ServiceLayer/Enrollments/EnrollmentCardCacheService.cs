using Common.Caching;
using Microsoft.Extensions.Options;

namespace ServiceLayer.Enrollments;

public sealed class EnrollmentCardCacheService(
    ICacheService cacheService,
    IOptions<EnrollmentCardCacheOptions> options) : IEnrollmentCardCacheService
{
    private readonly EnrollmentCardCacheOptions _options = options.Value;

    public async Task<EnrollmentCard?> GetOrCreateAsync(
        long userId,
        long enrollmentId,
        Func<CancellationToken, Task<EnrollmentCard?>> loadCardAsync,
        CancellationToken ct = default)
    {
        var (userVersion, enrollmentVersion) = await GetVersionsAsync(userId, enrollmentId, ct);
        var cacheKey = GetCardKey(userId, userVersion, enrollmentId, enrollmentVersion);

        var cachedCard = await cacheService.GetAsync<EnrollmentCard>(cacheKey, ct);
        if (cachedCard is not null)
            return cachedCard;

        var card = await loadCardAsync(ct);
        if (card is not null)
            await cacheService.SetAsync(cacheKey, card, _options.EnrollmentCardTtl, ct);

        return card;
    }

    public async Task InvalidateEnrollmentsForUserAsync(long userId, CancellationToken ct = default)
    {
        await cacheService.IncrementAsync(GetUserVersionKey(userId), ct);
    }

    public async Task InvalidateEnrollmentsForUsersAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken ct = default)
    {
        if (userIds.Count == 1)
        {
            await InvalidateEnrollmentsForUserAsync(userIds.First(), ct);
            return;
        }

        var keys = userIds
            .Select(GetUserVersionKey)
            .ToArray();

        await cacheService.IncrementManyAsync(keys, ct);
    }

    public async Task InvalidateEnrollmentForUserAsync(
        long userId,
        long enrollmentId,
        CancellationToken ct = default)
    {
        await cacheService.IncrementAsync(GetEnrollmentVersionKey(userId, enrollmentId), ct);
    }

    private static string GetCardKey(
        long userId,
        long userVersion,
        long enrollmentId,
        long enrollmentVersion) =>
        $"enrollment-card:{userId}:{userVersion}:{enrollmentId}:{enrollmentVersion}";

    private static string GetUserVersionKey(long userId) => $"enrollment-card-user-version:{userId}";

    private static string GetEnrollmentVersionKey(long userId, long enrollmentId) =>
        $"enrollment-card-enrollment-version:{userId}:{enrollmentId}";

    private async Task<(long UserVersion, long EnrollmentVersion)> GetVersionsAsync(
        long userId,
        long enrollmentId,
        CancellationToken ct)
    {
        var versions = await cacheService.GetManyAsync<long>(
            [GetUserVersionKey(userId), GetEnrollmentVersionKey(userId, enrollmentId)],
            ct);

        return (versions[0], versions[1]);
    }
}
