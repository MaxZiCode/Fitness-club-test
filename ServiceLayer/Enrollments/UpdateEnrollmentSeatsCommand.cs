namespace ServiceLayer.Enrollments;

public sealed record UpdateEnrollmentSeatsCommand(long EnrollmentId, int Seats, long UserId);
