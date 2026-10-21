namespace DataAccessLayer.Repositories;

public sealed record ClassLoadSummaryRow(
    DateTime Date,
    long RoomId,
    string RoomName,
    int ClassesCount,
    int TotalCapacity,
    int BookedSeats);
