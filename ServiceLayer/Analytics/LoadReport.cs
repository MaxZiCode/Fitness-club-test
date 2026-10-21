using DataAccessLayer.Repositories;

namespace ServiceLayer.Analytics;

public sealed record LoadReport(IReadOnlyList<LoadItem> Items);

public sealed record LoadItem(
    DateOnly Date,
    long RoomId,
    string RoomName,
    int ClassesCount,
    int TotalCapacity,
    int BookedSeats)
{
    public static LoadItem FromClassLoadSummaryRow(ClassLoadSummaryRow row) => new(
        DateOnly.FromDateTime(row.Date),
        row.RoomId,
        row.RoomName,
        row.ClassesCount,
        row.TotalCapacity,
        row.BookedSeats);
}
