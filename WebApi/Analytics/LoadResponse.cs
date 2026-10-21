using ServiceLayer.Analytics;

namespace WebApi.Analytics;

public sealed record LoadResponse(IReadOnlyList<LoadItemResponse> Items)
{
    public static LoadResponse FromReport(LoadReport report) =>
        new(report.Items.Select(LoadItemResponse.FromItem).ToArray());
}

public sealed record LoadItemResponse(
    DateOnly Date,
    LoadRoomResponse Room,
    int ClassesCount,
    int TotalCapacity,
    int BookedSeats)
{
    public static LoadItemResponse FromItem(LoadItem item) => new(
        item.Date,
        new LoadRoomResponse(item.RoomId, item.RoomName),
        item.ClassesCount,
        item.TotalCapacity,
        item.BookedSeats);
}

public sealed record LoadRoomResponse(long Id, string Name);
