using ErrorOr;

namespace ServiceLayer.Enrollments;

public static class ClassErrors
{
    public static readonly Error CapacityExceeded = Error.Conflict(
        code: "capacity-exceeded",
        description: "The class does not have enough available capacity.");

    public static readonly Error ClassStarted = Error.Conflict(
        code: "class-started",
        description: "The class has already started.");

    public static readonly Error ClassCancelled = Error.Conflict(
        code: "class-cancelled",
        description: "The class has been cancelled.");
}
