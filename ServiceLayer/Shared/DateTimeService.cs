namespace ServiceLayer.Shared;

public sealed class DateTimeService : IDateTimeService
{
    public DateTime GetNow() => DateTime.UtcNow;
}
