using DataAccessLayer.Entities;
using ServiceLayer.Enrollments;
using ServiceLayer.Enrollments.Waitlist;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class ClassValidationServiceTests
{
    private ClassValidationService _sut = null!;

    private Class _class = null!;

    private DateTime _now;
    private int _classCapacity;
    private int _bookedSeats;
    private int _availableSeats;
    private int _seatIncrease;
    private int _seatDecrease;
    private int _requestedSeats;

    [TestInitialize]
    public void Setup()
    {
        _now = new DateTime(
            2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        _classCapacity = 10;
        _bookedSeats = 6;
        _availableSeats = _classCapacity - _bookedSeats;
        _seatIncrease = 2;
        _seatDecrease = 1;
        _requestedSeats = _availableSeats;

        _class = new Class
        {
            Capacity = _classCapacity,
            StartsAt = _now.AddHours(2),
            IsCancelled = false
        };

        _sut = new ClassValidationService();
    }

    [TestMethod]
    public void ValidateEnrollment_ClassCancelled_ReturnsClassCancelled()
    {
        _class.IsCancelled = true;
        _class.StartsAt = _now;

        var seatsDiff = _availableSeats + _seatIncrease;

        var result = _sut.ValidateEnrollment(
            _class,
            _bookedSeats,
            seatsDiff,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.ClassCancelled.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollment_ClassStartsAtNow_ReturnsClassStarted()
    {
        _class.StartsAt = _now;

        var result = _sut.ValidateEnrollment(
            _class,
            _bookedSeats,
            _seatIncrease,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.ClassStarted.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollment_ClassStartedInThePast_ReturnsClassStarted()
    {
        _class.StartsAt = _now.Subtract(TimeSpan.FromSeconds(1));

        var result = _sut.ValidateEnrollment(
            _class,
            _bookedSeats,
            _seatIncrease,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.ClassStarted.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollment_IncreaseExceedsAvailableCapacity_ReturnsCapacityExceeded()
    {
        var seatsDiff = _availableSeats + _seatIncrease;

        var result = _sut.ValidateEnrollment(
            _class,
            _bookedSeats,
            seatsDiff,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.CapacityExceeded.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollment_IncreaseExactlyFitsAvailableCapacity_ReturnsSuccess()
    {
        var result = _sut.ValidateEnrollment(
            _class,
            _bookedSeats,
            _availableSeats,
            _now);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollment_IncreaseFitsWithOneSeatRemaining_ReturnsSuccess()
    {
        var seatsDiff = _availableSeats - _seatDecrease;

        var result = _sut.ValidateEnrollment(
            _class,
            _bookedSeats,
            seatsDiff,
            _now);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollment_IncreaseWhenBookedSeatsExceedCapacity_ReturnsCapacityExceeded()
    {
        var bookedSeats = _classCapacity + _seatDecrease;

        var result = _sut.ValidateEnrollment(
            _class,
            bookedSeats,
            _seatIncrease,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.CapacityExceeded.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollment_ZeroSeatDifferenceWhenClassIsFull_ReturnsSuccess()
    {
        var bookedSeats = _classCapacity;

        var result = _sut.ValidateEnrollment(
            _class,
            bookedSeats,
            0,
            _now);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollment_DecreasingSeatsWhenClassIsFull_ReturnsSuccess()
    {
        var bookedSeats = _classCapacity;
        var seatsDiff = -_seatDecrease;

        var result = _sut.ValidateEnrollment(
            _class,
            bookedSeats,
            seatsDiff,
            _now);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateWaitlist_ClassCancelled_ReturnsClassCancelled()
    {
        _class.IsCancelled = true;
        _class.StartsAt = _now;

        var result = _sut.ValidateWaitlist(
            _class,
            _bookedSeats,
            _requestedSeats,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.ClassCancelled.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateWaitlist_ClassStartsAtNow_ReturnsClassStarted()
    {
        _class.StartsAt = _now;

        var result = _sut.ValidateWaitlist(
            _class,
            _bookedSeats,
            _requestedSeats,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.ClassStarted.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateWaitlist_RequestedSeatsEqualAvailableCapacity_ReturnsSeatsAvailable()
    {
        var result = _sut.ValidateWaitlist(
            _class,
            _bookedSeats,
            _availableSeats,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            WaitlistErrors.SeatsAvailable.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateWaitlist_RequestedSeatsBelowAvailableCapacity_ReturnsSeatsAvailable()
    {
        var requestedSeats = _availableSeats - _seatDecrease;

        var result = _sut.ValidateWaitlist(
            _class,
            _bookedSeats,
            requestedSeats,
            _now);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            WaitlistErrors.SeatsAvailable.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateWaitlist_RequestedSeatsExceedAvailableCapacity_ReturnsSuccess()
    {
        var requestedSeats = _availableSeats + _seatIncrease;

        var result = _sut.ValidateWaitlist(
            _class,
            _bookedSeats,
            requestedSeats,
            _now);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateWaitlist_NoAvailableSeats_ReturnsSuccess()
    {
        var bookedSeats = _classCapacity;
        var requestedSeats = _seatIncrease;

        var result = _sut.ValidateWaitlist(
            _class,
            bookedSeats,
            requestedSeats,
            _now);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateWaitlist_BookedSeatsExceedCapacity_ReturnsSuccess()
    {
        var bookedSeats = _classCapacity + _seatDecrease;
        var requestedSeats = _seatIncrease;

        var result = _sut.ValidateWaitlist(
            _class,
            bookedSeats,
            requestedSeats,
            _now);

        Assert.IsFalse(result.IsError);
    }
}
