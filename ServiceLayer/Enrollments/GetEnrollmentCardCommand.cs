namespace ServiceLayer.Enrollments;

public sealed record GetEnrollmentCardCommand(long EnrollmentId, long UserId);
