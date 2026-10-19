namespace ServiceLayer.Enrollments;

public sealed class EnrollmentCardCacheOptions
{
    public TimeSpan? EnrollmentCardTtl { get; set; }

    public TimeSpan? UserVersionTtl { get; set; }
}
