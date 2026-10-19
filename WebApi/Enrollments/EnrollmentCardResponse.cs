using ServiceLayer.Enrollments;

namespace WebApi.Enrollments;

public sealed record EnrollmentCardResponse(
    long Id,
    string Status,
    int Seats,
    DateTime CreatedAt,
    EnrollmentCardClassResponse Class,
    EnrollmentCardPassResponse Pass)
{
    public static EnrollmentCardResponse FromCard(EnrollmentCard card) =>
        new(
            card.Id,
            card.Status,
            card.Seats,
            card.CreatedAt,
            EnrollmentCardClassResponse.FromClass(card.Class),
            EnrollmentCardPassResponse.FromPass(card.Pass));
}

public sealed record EnrollmentCardClassResponse(
    long Id,
    string Title,
    DateTime StartsAt,
    int DurationMinutes,
    bool IsCancelled,
    EnrollmentCardRoomResponse Room)
{
    public static EnrollmentCardClassResponse FromClass(EnrollmentCardClass @class) =>
        new(
            @class.Id,
            @class.Title,
            @class.StartsAt,
            @class.DurationMinutes,
            @class.IsCancelled,
            EnrollmentCardRoomResponse.FromRoom(@class.Room));
}

public sealed record EnrollmentCardRoomResponse(long Id, string Name)
{
    public static EnrollmentCardRoomResponse FromRoom(EnrollmentCardRoom room) => new(room.Id, room.Name);
}

public sealed record EnrollmentCardPassResponse(int RemainingVisits, DateOnly ValidUntil)
{
    public static EnrollmentCardPassResponse FromPass(EnrollmentCardPass pass) =>
        new(pass.RemainingVisits, pass.ValidUntil);
}
