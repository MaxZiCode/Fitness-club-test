using DataAccessLayer.Entities;

namespace ServiceLayer.Enrollments;

public sealed record EnrollmentCard(
    long Id,
    string Status,
    int Seats,
    DateTime CreatedAt,
    EnrollmentCardClass Class,
    EnrollmentCardPass Pass)
{
    public static EnrollmentCard FromEnrollment(Enrollment enrollment, Pass pass) =>
        new(
            enrollment.Id,
            enrollment.Status,
            enrollment.Seats,
            enrollment.CreatedAt,
            EnrollmentCardClass.FromClass(enrollment.Class),
            EnrollmentCardPass.FromPass(pass));
}

public sealed record EnrollmentCardClass(
    long Id,
    string Title,
    DateTime StartsAt,
    int DurationMinutes,
    bool IsCancelled,
    EnrollmentCardRoom Room)
{
    public static EnrollmentCardClass FromClass(Class @class) =>
        new(
            @class.Id,
            @class.Title,
            @class.StartsAt,
            @class.DurationMinutes,
            @class.IsCancelled,
            EnrollmentCardRoom.FromRoom(@class.Room));
}

public sealed record EnrollmentCardRoom(long Id, string Name)
{
    public static EnrollmentCardRoom FromRoom(Room room) => new(room.Id, room.Name);
}

public sealed record EnrollmentCardPass(int RemainingVisits, DateOnly ValidUntil)
{
    public static EnrollmentCardPass FromPass(Pass pass) => new(pass.RemainingVisits, pass.ValidUntil);
}
