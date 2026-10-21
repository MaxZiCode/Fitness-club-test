namespace DataAccessLayer.Repositories;

public sealed record ScheduleClassRow(
    long ClassId,
    string Title,
    DateTime StartsAt,
    int DurationMinutes,
    long RoomId,
    string RoomName,
    int Capacity,
    int BookedSeats,
    int MySeats);
