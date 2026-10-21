namespace ServiceLayer.Analytics;

public sealed record LoadQuery(DateOnly From, DateOnly To, long? RoomId);
