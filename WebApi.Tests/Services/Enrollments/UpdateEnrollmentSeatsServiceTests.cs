using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using ServiceLayer.Enrollments;
using ServiceLayer.Shared;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class UpdateEnrollmentSeatsServiceTests
{
    private long _userId;
    private int _bookedSeats;
    private int _initialEnrollmentSeats;
    private int _initialRemainingVisits;
    private int _seatIncrease;
    private int _seatDecrease;
    private int _increasedSeats;
    private int _decreasedSeats;
    private int _expectedRemainingVisitsAfterIncrease;
    private int _expectedRemainingVisitsAfterDecrease;
    private DateTime _now;

    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IDbContextTransaction> _transaction = null!;

    private Mock<IDateTimeService> _dateTimeService = null!;
    private Mock<IPassValidationService> _passValidationService = null!;
    private Mock<IClassValidationService> _classValidationService = null!;
    private Mock<IEnrollmentValidationService> _enrollmentValidationService = null!;
    private Mock<IEnrollmentCardCacheService> _cacheService = null!;

    private UpdateEnrollmentSeatsService _sut = null!;

    private Class _class = null!;
    private Enrollment _enrollment = null!;
    private Pass _pass = null!;
    private List<string> _lockOrder = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 1;
        _bookedSeats = 3;
        _initialEnrollmentSeats = 2;
        _initialRemainingVisits = 10;
        _seatIncrease = 2;
        _seatDecrease = 1;
        _increasedSeats = _initialEnrollmentSeats + _seatIncrease;
        _decreasedSeats = _initialEnrollmentSeats - _seatDecrease;

        _expectedRemainingVisitsAfterIncrease =
            _initialRemainingVisits - _seatIncrease;

        _expectedRemainingVisitsAfterDecrease =
            _initialRemainingVisits + _seatDecrease;

        _now = new DateTime(
            2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        _lockOrder = [];

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _transaction = new Mock<IDbContextTransaction>();

        _dateTimeService = new Mock<IDateTimeService>();
        _passValidationService = new Mock<IPassValidationService>();
        _classValidationService = new Mock<IClassValidationService>();
        _enrollmentValidationService =
            new Mock<IEnrollmentValidationService>();
        _cacheService = new Mock<IEnrollmentCardCacheService>();

        _unitOfWork
            .Setup(x => x.BeginTransactionAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object);

        _transaction
            .Setup(x => x.CommitAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _class = new Class
        {
            Id = 2,
            Room = new Room()
            {
                Id = 5,
                Name = "Room"
            }
        };

        _enrollment = new Enrollment
        {
            Id = 3,
            UserId = _userId,
            ClassId = _class.Id,
            Seats = _initialEnrollmentSeats,
            InitialSeats = _initialEnrollmentSeats,
            Status = EnrollmentStatuses.Active,
            CreatedAt = _now
        };

        _pass = new Pass
        {
            UserId = _userId,
            RemainingVisits = _initialRemainingVisits
        };

        _dateTimeService
            .Setup(x => x.GetNow())
            .Returns(_now);

        var classRepository = Mock.Get(_unitOfWork.Object.Class);

        classRepository
            .Setup(x => x.GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => _lockOrder.Add(nameof(Class)))
            .ReturnsAsync(_class);

        classRepository
            .Setup(x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_bookedSeats);

        var enrollmentRepository =
            Mock.Get(_unitOfWork.Object.Enrollment);

        enrollmentRepository
            .Setup(x => x.GetByIdAndUserAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_enrollment);

        var passRepository = Mock.Get(_unitOfWork.Object.Pass);

        passRepository
            .Setup(x => x.GetLockedByUserIdAsync(
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => _lockOrder.Add(nameof(Pass)))
            .ReturnsAsync(_pass);

        _sut = new UpdateEnrollmentSeatsService(
            _unitOfWork.Object,
            _dateTimeService.Object,
            _passValidationService.Object,
            _classValidationService.Object,
            _enrollmentValidationService.Object,
            _cacheService.Object);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_ValidCommand_UpdatesSeatsAndPassVisits()
    {
        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_increasedSeats, _enrollment.Seats);
        Assert.AreEqual(
            _expectedRemainingVisitsAfterIncrease,
            _pass.RemainingVisits);

        _classValidationService.Verify(
            x => x.ValidateEnrollment(
                _class,
                _bookedSeats,
                _seatIncrease,
                _now),
            Times.Once);

        _enrollmentValidationService.Verify(
            x => x.Validate(_enrollment),
            Times.Once);

        _passValidationService.Verify(
            x => x.ValidateEnrollmentUpdate(
                _pass,
                _seatIncrease,
                DateOnly.FromDateTime(_now)),
            Times.Once);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_ClassNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Class?)null);

        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            Error.NotFound().Code,
            result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.GetByIdAndUserAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_EnrollmentNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.GetByIdAndUserAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enrollment?)null);

        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            Error.NotFound().Code,
            result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_ClassValidationFails_ReturnsError()
    {
        var validationError = Error.Validation(
            "Class.Unavailable",
            "The class is not available for enrollment.");

        _classValidationService
            .Setup(x => x.ValidateEnrollment(
                _class,
                _bookedSeats,
                _seatIncrease,
                _now))
            .Returns(validationError);

        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            validationError.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            _initialEnrollmentSeats,
            _enrollment.Seats);

        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        _enrollmentValidationService.Verify(
            x => x.Validate(It.IsAny<Enrollment>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_EnrollmentValidationFails_ReturnsError()
    {
        var validationError = Error.Validation(
            "Enrollment.Invalid",
            "The enrollment cannot be updated.");

        _enrollmentValidationService
            .Setup(x => x.Validate(_enrollment))
            .Returns(validationError);

        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            validationError.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            _initialEnrollmentSeats,
            _enrollment.Seats);

        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_PassValidationFails_ReturnsError()
    {
        var validationError = Error.Validation(
            "Pass.InsufficientVisits",
            "The pass does not have enough visits.");

        _passValidationService
            .Setup(x => x.ValidateEnrollmentUpdate(
                _pass,
                _seatIncrease,
                DateOnly.FromDateTime(_now)))
            .Returns(validationError);

        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            validationError.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            _initialEnrollmentSeats,
            _enrollment.Seats);

        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_RequestedSeatsUnchanged_ReturnsEnrollmentCard()
    {
        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _initialEnrollmentSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            _initialEnrollmentSeats,
            _enrollment.Seats);

        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_DecreasingSeats_RestoresPassVisits()
    {
        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _decreasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            _decreasedSeats,
            _enrollment.Seats);

        Assert.AreEqual(
            _expectedRemainingVisitsAfterDecrease,
            _pass.RemainingVisits);

        _classValidationService.Verify(
            x => x.ValidateEnrollment(
                _class,
                _bookedSeats,
                -_seatDecrease,
                _now),
            Times.Once);

        _passValidationService.Verify(
            x => x.ValidateEnrollmentUpdate(
                _pass,
                -_seatDecrease,
                DateOnly.FromDateTime(_now)),
            Times.Once);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateSeatsAsync_LocksClassBeforePass()
    {
        var command = new UpdateEnrollmentSeatsCommand(
            _enrollment.Id,
            _increasedSeats,
            _userId);

        var result = await _sut.UpdateSeatsAsync(command);

        Assert.IsFalse(result.IsError);

        CollectionAssert.AreEqual(
            new[] { nameof(Class), nameof(Pass) },
            _lockOrder);
    }
}
