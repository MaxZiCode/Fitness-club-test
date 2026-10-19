namespace ServiceLayer.Enrollments;

public interface IEnrollmentCardCacheService
{
    Task<EnrollmentCard?> GetOrCreateAsync(
        long userId,
        long enrollmentId,
        Func<CancellationToken, Task<EnrollmentCard?>> loadCardAsync,
        CancellationToken cancellationToken = default);

    Task InvalidateEnrollmentsForUserAsync(long userId, CancellationToken cancellationToken = default);
}
