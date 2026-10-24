namespace ServiceLayer.Shared;

public sealed class DateTimeService : IDateTimeService
{
    public DateTime GetNow()
    {
        var utcNow = DateTime.UtcNow;
        // решение: приводим время к точности PostgreSQL, чтобы значение в памяти совпадало с сохранённым.
        return new DateTime(utcNow.Ticks - utcNow.Ticks % 10, DateTimeKind.Utc);
    }
}
