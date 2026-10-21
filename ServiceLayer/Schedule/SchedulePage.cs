using DataAccessLayer.Repositories;

namespace ServiceLayer.Schedule;

public sealed record SchedulePage(IReadOnlyList<ScheduleItem> Items, string? ContinuationToken);

public sealed record ScheduleItem(
    long ClassId,
    string Title,
    DateTime StartsAt,
    int DurationMinutes,
    long RoomId,
    string RoomName,
    int Capacity,
    int FreeSeats,
    int MySeats)
{
    public static ScheduleItem FromRow(ScheduleClassRow row) => new(
        row.ClassId,
        row.Title,
        row.StartsAt,
        row.DurationMinutes,
        row.RoomId,
        row.RoomName,
        row.Capacity,
        row.Capacity - row.BookedSeats,
        row.MySeats);
}
