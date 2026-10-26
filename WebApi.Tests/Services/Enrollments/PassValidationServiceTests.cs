using DataAccessLayer.Entities;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class PassValidationServiceTests
{
    private PassValidationService _sut = null!;
    private Pass _pass = null!;

    private DateOnly _today;
    private DateOnly _validUntil;
    private int _initialRemainingVisits;
    private int _requestedSeats;
    private int _difference;

    [TestInitialize]
    public void Setup()
    {
        _today = new DateOnly(2026, 10, 26);
        _validUntil = _today.AddDays(1);
        _initialRemainingVisits = 10;
        _requestedSeats = 3;
        _difference = 2;

        _pass = new Pass
        {
            UserId = 1,
            RemainingVisits = _initialRemainingVisits,
            ValidUntil = _validUntil
        };

        _sut = new PassValidationService();
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_ValidPassAndEnoughVisits_ReturnsSuccess()
    {
        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            _requestedSeats,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_PassExpired_ReturnsPassExpired()
    {
        _pass.ValidUntil = _today.AddDays(-1);

        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            _requestedSeats,
            _today);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            PassErrors.PassExpired.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_PassValidUntilToday_ReturnsSuccess()
    {
        _pass.ValidUntil = _today;

        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            _requestedSeats,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_PassExpiredAndExhausted_ReturnsPassExpired()
    {
        _pass.ValidUntil = _today.AddDays(-1);
        _pass.RemainingVisits = _requestedSeats - 1;

        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            _requestedSeats,
            _today);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            PassErrors.PassExpired.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_NotEnoughVisits_ReturnsPassExhausted()
    {
        _pass.RemainingVisits = _requestedSeats - 1;

        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            _requestedSeats,
            _today);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            PassErrors.PassExhausted.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_RemainingVisitsEqualRequestedSeats_ReturnsSuccess()
    {
        _pass.RemainingVisits = _requestedSeats;

        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            _requestedSeats,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_ZeroRequestedSeats_ReturnsSuccess()
    {
        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            0,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentCreate_NegativeRequestedSeats_ReturnsSuccess()
    {
        var result = _sut.ValidateEnrollmentCreate(
            _pass,
            -_requestedSeats,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_PositiveDifferenceAndValidPass_ReturnsSuccess()
    {
        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            _difference,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_PositiveDifferenceAndExpiredPass_ReturnsPassExpired()
    {
        _pass.ValidUntil = _today.AddDays(-1);

        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            _difference,
            _today);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            PassErrors.PassExpired.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_PositiveDifferenceAndNotEnoughVisits_ReturnsPassExhausted()
    {
        _pass.RemainingVisits = _difference - 1;

        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            _difference,
            _today);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            PassErrors.PassExhausted.Code,
            result.FirstError.Code);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_PositiveDifferenceAndRemainingVisitsEqualDifference_ReturnsSuccess()
    {
        _pass.RemainingVisits = _difference;

        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            _difference,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_ZeroDifference_ReturnsSuccessEvenIfPassExpiredAndExhausted()
    {
        _pass.ValidUntil = _today.AddDays(-1);
        _pass.RemainingVisits = 0;

        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            0,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_NegativeDifference_ReturnsSuccessEvenIfPassExpiredAndExhausted()
    {
        _pass.ValidUntil = _today.AddDays(-1);
        _pass.RemainingVisits = 0;

        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            -_difference,
            _today);

        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void ValidateEnrollmentUpdate_ExpiredAndExhaustedPass_ReturnsPassExpired()
    {
        _pass.ValidUntil = _today.AddDays(-1);
        _pass.RemainingVisits = _difference - 1;

        var result = _sut.ValidateEnrollmentUpdate(
            _pass,
            _difference,
            _today);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            PassErrors.PassExpired.Code,
            result.FirstError.Code);
    }
}
