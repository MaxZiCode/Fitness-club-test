namespace ServiceLayer.Enrollments;

public static class EnrollmentErrors
{
    public static class Conflict
    {
        public const string CapacityExceeded = "capacity-exceeded";
        public const string PassExhausted = "pass-exhausted";
        public const string PassExpired = "pass-expired";
        public const string ClassStarted = "class-started";
        public const string ClassCancelled = "class-cancelled";
    }
}
