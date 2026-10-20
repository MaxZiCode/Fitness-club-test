namespace ServiceLayer.Enrollments;

public sealed record CancelEnrollmentCommand(long EnrollmentId, long UserId);
