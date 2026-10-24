namespace ServiceLayer.Enrollments;

public sealed class EnrollmentCancellationOptions
{
    public const string SectionName = "EnrollmentCancellation";

    public TimeSpan RefundCutoff { get; set; } = TimeSpan.FromHours(2);
}
