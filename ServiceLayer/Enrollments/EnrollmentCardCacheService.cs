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
        var version = await GetUserVersionAsync(userId, ct);
        var cacheKey = GetCardKey(userId, enrollmentId, version);
        var cachedCard = await cacheService.GetAsync<EnrollmentCard>(cacheKey, ct);
        if (cachedCard is not null)
            return cachedCard;

        var card = await loadCardAsync(ct);
        if (card is not null)
            await cacheService.SetAsync(cacheKey, card, _options.EnrollmentCardTtl, ct);

        return card;
    }

    public Task InvalidateEnrollmentsForUserAsync(long userId, CancellationToken ct = default) =>
        cacheService.SetAsync(
            GetUserVersionKey(userId),
            CreateVersion(),
            _options.UserVersionTtl,
            ct);

    private async Task<string> GetUserVersionAsync(long userId, CancellationToken ct)
    {
        var versionKey = GetUserVersionKey(userId);
        var version = await cacheService.GetAsync<string>(versionKey, ct);
        if (version is not null)
            return version;

        version = CreateVersion();
        await cacheService.SetAsync(
            versionKey,
            version,
            _options.UserVersionTtl,
            ct);

        return version;
    }

    private static string GetUserVersionKey(long userId) => $"user-version:{userId}";

    private static string GetCardKey(long userId, long enrollmentId, string userVersion) =>
        $"enrollment-card:{userId}:{enrollmentId}:{userVersion}";

    private static string CreateVersion() => Guid.NewGuid().ToString("N");
}
