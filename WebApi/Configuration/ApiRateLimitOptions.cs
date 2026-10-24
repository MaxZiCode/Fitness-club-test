namespace WebApi.Configuration;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int RequestsPerMinute { get; set; } = 60;
}
