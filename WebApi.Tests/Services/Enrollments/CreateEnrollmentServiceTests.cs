using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using ServiceLayer.Enrollments;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class CreateEnrollmentServiceTests
{
    private long _userId;
    private string _idempotencyKey = null!;
    private string _keyHash = null!;
    private int _bookedSeats;
    private int _seatsToBook;
    private int _initialRemainingVisits;
    private int _expectedRemainingVisitsAfterBooking;
    private int _changedEnrollmentSeats;
    private DateTime _now;

    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IDbContextTransaction> _transaction = null!;

    private Mock<IDateTimeService> _dateTimeService = null!;
    private Mock<IIdempotencyKeyHasher> _idempotencyKeyHasher = null!;
    private Mock<IPassValidationService> _passValidationService = null!;
    private Mock<IClassValidationService> _classValidationService = null!;
    private Mock<IEnrollmentCardCacheService> _cacheService = null!;

    private CreateEnrollmentService _sut = null!;

    private Class _class = null!;
    private Pass _pass = null!;
    private List<string> _lockOrder = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 1;
        _idempotencyKey = "test-idempotency-key";
        _keyHash = "test-key-hash";
        _bookedSeats = 3;
        _seatsToBook = 2;
        _initialRemainingVisits = 10;
        _expectedRemainingVisitsAfterBooking =
            _initialRemainingVisits - _seatsToBook;
        _changedEnrollmentSeats = _seatsToBook - 1;
        _now = new DateTime(
            2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        _lockOrder = [];

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _transaction = new Mock<IDbContextTransaction>();

        _dateTimeService = new Mock<IDateTimeService>();
        _idempotencyKeyHasher = new Mock<IIdempotencyKeyHasher>();
        _passValidationService = new Mock<IPassValidationService>();
        _classValidationService = new Mock<IClassValidationService>();
        _cacheService = new Mock<IEnrollmentCardCacheService>();

        _unitOfWork
            .Setup(x => x.BeginTransactionAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object);

        _transaction
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _class = new Class
        {
            Id = 2
        };

        _pass = new Pass
        {
            UserId = _userId,
            RemainingVisits = _initialRemainingVisits
        };

        _dateTimeService
            .Setup(x => x.GetNow())
            .Returns(_now);

        _idempotencyKeyHasher
            .Setup(x => x.Hash(_idempotencyKey))
            .Returns(_keyHash);

        var classRepository = Mock.Get(_unitOfWork.Object.Class);

        classRepository
            .Setup(x => x.GetLockedNoTrackingIgnoringFiltersAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .Callback(() => _lockOrder.Add(nameof(Class)))
            .ReturnsAsync(_class);

        classRepository
            .Setup(x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_bookedSeats);

        var passRepository = Mock.Get(_unitOfWork.Object.Pass);

        passRepository
            .Setup(x => x.GetLockedByUserIdAsync(
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => _lockOrder.Add(nameof(Pass)))
            .ReturnsAsync(_pass);

        var enrollmentRepository =
            Mock.Get(_unitOfWork.Object.Enrollment);

        enrollmentRepository
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enrollment?)null);

        _sut = new CreateEnrollmentService(
            _unitOfWork.Object,
            _dateTimeService.Object,
            _idempotencyKeyHasher.Object,
            _passValidationService.Object,
            _classValidationService.Object,
            _cacheService.Object);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_ValidCommand_CreatesEnrollment()
    {
        Enrollment? createdEnrollment = null;

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.Create(It.IsAny<Enrollment>()))
            .Callback<Enrollment>(enrollment =>
                createdEnrollment = enrollment);

        var command = new CreateEnrollmentCommand(
            _class.Id,
            _seatsToBook,
            _idempotencyKey,
            _userId);

        var result = await _sut.CreateEnrollmentAsync(command);

        Assert.IsFalse(result.IsError);
        Assert.IsNotNull(createdEnrollment);

        Assert.AreEqual(_userId, createdEnrollment.UserId);
        Assert.AreEqual(_class.Id, createdEnrollment.ClassId);
        Assert.AreEqual(_seatsToBook, createdEnrollment.Seats);
        Assert.AreEqual(_seatsToBook, createdEnrollment.InitialSeats);
        Assert.AreEqual(
            EnrollmentStatuses.Active,
            createdEnrollment.Status);
        Assert.AreEqual(_keyHash, createdEnrollment.IdempotencyKeyHash);
        Assert.AreEqual(_now, createdEnrollment.CreatedAt);

        Assert.AreEqual(
            _expectedRemainingVisitsAfterBooking,
            _pass.RemainingVisits);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_ClassNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedNoTrackingIgnoringFiltersAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Class?)null);

        var result = await _sut.CreateEnrollmentAsync(
            new CreateEnrollmentCommand(
                _class.Id,
                _seatsToBook,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(Error.NotFound().Code, result.FirstError.Code);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _passValidationService.Verify(
            x => x.ValidateEnrollmentCreate(
                It.IsAny<Pass>(),
                It.IsAny<int>(),
                It.IsAny<DateOnly>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_PassValidationFails_ReturnsError()
    {
        var validationError = Error.Validation(
            "Pass.Invalid",
            "The pass is not valid for enrollment.");

        _passValidationService
            .Setup(x => x.ValidateEnrollmentCreate(
                _pass,
                _seatsToBook,
                DateOnly.FromDateTime(_now)))
            .Returns(validationError);

        var result = await _sut.CreateEnrollmentAsync(
            new CreateEnrollmentCommand(
                _class.Id,
                _seatsToBook,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(validationError.Code, result.FirstError.Code);
        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_ClassValidationFails_ReturnsError()
    {
        var validationError = Error.Validation(
            "Class.Unavailable",
            "The class is not available for enrollment.");

        _classValidationService
            .Setup(x => x.ValidateEnrollment(
                _class,
                _bookedSeats,
                _seatsToBook,
                _now))
            .Returns(validationError);

        var result = await _sut.CreateEnrollmentAsync(
            new CreateEnrollmentCommand(
                _class.Id,
                _seatsToBook,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(validationError.Code, result.FirstError.Code);
        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        _classValidationService.Verify(
            x => x.ValidateEnrollment(
                _class,
                _bookedSeats,
                _seatsToBook,
                _now),
            Times.Once);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_ExistingKeyAndSamePayload_RestoresOriginalSeatsAndStatus()
    {
        Enrollment? storedEnrollment = null;
        var enrollmentRepository =
            Mock.Get(_unitOfWork.Object.Enrollment);

        enrollmentRepository
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(storedEnrollment));

        enrollmentRepository
            .Setup(x => x.Create(It.IsAny<Enrollment>()))
            .Callback<Enrollment>(enrollment =>
                storedEnrollment = enrollment);

        var command = new CreateEnrollmentCommand(
            _class.Id,
            _seatsToBook,
            _idempotencyKey,
            _userId);

        var createResult = await _sut.CreateEnrollmentAsync(command);

        Assert.IsFalse(createResult.IsError);
        Assert.IsNotNull(storedEnrollment);
        Assert.AreEqual(_seatsToBook, storedEnrollment.Seats);
        Assert.AreEqual(
            EnrollmentStatuses.Active,
            storedEnrollment.Status);

        storedEnrollment.Seats = _changedEnrollmentSeats;
        storedEnrollment.Status = EnrollmentStatuses.Cancelled;

        Assert.AreEqual(
            _changedEnrollmentSeats,
            storedEnrollment.Seats);
        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            storedEnrollment.Status);

        var replayResult = await _sut.CreateEnrollmentAsync(command);

        Assert.IsFalse(replayResult.IsError);
        Assert.AreEqual(_seatsToBook, storedEnrollment.Seats);
        Assert.AreEqual(
            EnrollmentStatuses.Active,
            storedEnrollment.Status);
        Assert.AreEqual(
            _expectedRemainingVisitsAfterBooking,
            _pass.RemainingVisits);

        enrollmentRepository.Verify(
            x => x.Create(It.IsAny<Enrollment>()),
            Times.Once);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _classValidationService.Verify(
            x => x.ValidateEnrollment(
                It.IsAny<Class>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<DateTime>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_ExistingKeyAndDifferentPayload_ReturnsPayloadMismatch()
    {
        var existingEnrollment = new Enrollment
        {
            UserId = _userId,
            ClassId = _class.Id + 1,
            InitialSeats = _seatsToBook,
            Seats = _changedEnrollmentSeats,
            Status = EnrollmentStatuses.Cancelled,
            IdempotencyKeyHash = _keyHash,
            CreatedAt = _now.AddDays(-1)
        };

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEnrollment);

        var result = await _sut.CreateEnrollmentAsync(
            new CreateEnrollmentCommand(
                _class.Id,
                _seatsToBook,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            IdempotencyErrors.PayloadMismatch.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            _changedEnrollmentSeats,
            existingEnrollment.Seats);
        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            existingEnrollment.Status);
        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.Create(It.IsAny<Enrollment>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUserAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CreateEnrollmentAsync_LocksClassBeforePass()
    {
        var result = await _sut.CreateEnrollmentAsync(
            new CreateEnrollmentCommand(
                _class.Id,
                _seatsToBook,
                _idempotencyKey,
                _userId));

        Assert.IsFalse(result.IsError);

        CollectionAssert.AreEqual(
            new[] { nameof(Class), nameof(Pass) },
            _lockOrder);
    }
}
