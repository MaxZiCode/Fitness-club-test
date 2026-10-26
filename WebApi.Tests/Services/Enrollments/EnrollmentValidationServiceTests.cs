using DataAccessLayer.Entities;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class EnrollmentValidationServiceTests
{
    private EnrollmentValidationService _sut = null!;
    private Enrollment _enrollment = null!;

    [TestInitialize]
    public void Setup()
    {
        _enrollment = new Enrollment
        {
            Id = 1,
            UserId = 2,
            ClassId = 3,
            Seats = 2,
            InitialSeats = 2,
            Status = EnrollmentStatuses.Active,
            CreatedAt = new DateTime(
                2026, 10, 26, 12, 0, 0, DateTimeKind.Utc)
        };

        _sut = new EnrollmentValidationService();
    }

    [TestMethod]
    public void Validate_ActiveEnrollment_ReturnsSuccess()
    {
        var result = _sut.Validate(_enrollment);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void Validate_CancelledEnrollment_ReturnsEnrollmentCancelled()
    {
        _enrollment.Status = EnrollmentStatuses.Cancelled;

        var result = _sut.Validate(_enrollment);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            EnrollmentErrors.EnrollmentCancelled.Code,
            result.FirstError.Code);
    }
}
