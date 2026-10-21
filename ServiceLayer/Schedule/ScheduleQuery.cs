namespace ServiceLayer.Schedule;

public sealed record ScheduleQuery(
    DateTime From,
    DateTime To,
    long? RoomId,
    int Limit,
    string? ContinuationToken,
    long UserId);
