using ServiceLayer.Schedule;

namespace WebApi.Schedule;

public sealed record ScheduleResponse(IReadOnlyList<ScheduleItemResponse> Items, string? ContinuationToken)
{
    public static ScheduleResponse FromPage(SchedulePage page) =>
        new(page.Items.Select(ScheduleItemResponse.FromItem).ToArray(), page.ContinuationToken);
}

public sealed record ScheduleItemResponse(
    long ClassId,
    string Title,
    DateTime StartsAt,
    int DurationMinutes,
    ScheduleRoomResponse Room,
    int Capacity,
    int FreeSeats,
    int MySeats)
{
    public static ScheduleItemResponse FromItem(ScheduleItem item) => new(
        item.ClassId, item.Title, item.StartsAt, item.DurationMinutes,
        new ScheduleRoomResponse(item.RoomId, item.RoomName), item.Capacity, item.FreeSeats, item.MySeats);
}

public sealed record ScheduleRoomResponse(long Id, string Name);
