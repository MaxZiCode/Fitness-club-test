using ErrorOr;

namespace ServiceLayer.Enrollments;

public static class EnrollmentErrors
{
    public static readonly Error EnrollmentCancelled = Error.Conflict(
        code: "enrollment-cancelled",
        description: "The enrollment has already been cancelled.");
}
