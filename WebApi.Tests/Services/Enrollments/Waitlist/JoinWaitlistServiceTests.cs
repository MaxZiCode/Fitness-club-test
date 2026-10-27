using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using ServiceLayer.Enrollments;
using ServiceLayer.Enrollments.Waitlist;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace WebApi.Tests.Services.Enrollments.Waitlist;

[TestClass]
public sealed class JoinWaitlistServiceTests
{
    private long _userId;
    private int _seats;
    private int _bookedSeats;
    private int _waitingCount;
    private int _expectedPosition;
    private DateTime _now;
    private string _idempotencyKey = null!;
    private string _keyHash = null!;

    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IDbContextTransaction> _transaction = null!;
    private Mock<IDateTimeService> _dateTimeService = null!;
    private Mock<IIdempotencyKeyHasher> _idempotencyKeyHasher = null!;
    private Mock<IClassValidationService> _classValidationService = null!;

    private JoinWaitlistService _sut = null!;

    private Class _class = null!;
    private List<string> _operationOrder = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 1;
        _seats = 2;
        _bookedSeats = 10;
        _waitingCount = 4;
        _expectedPosition = _waitingCount + 1;
        _now = new DateTime(
            2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        _idempotencyKey = "test-idempotency-key";
        _keyHash = "test-key-hash";
        _operationOrder = [];

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _transaction = new Mock<IDbContextTransaction>();
        _dateTimeService = new Mock<IDateTimeService>();
        _idempotencyKeyHasher = new Mock<IIdempotencyKeyHasher>();
        _classValidationService = new Mock<IClassValidationService>();

        _class = new Class
        {
            Id = 2,
            Capacity = 10,
            StartsAt = _now.AddDays(1),
            IsCancelled = false
        };

        _unitOfWork
            .Setup(x => x.BeginTransactionAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object);

        _transaction
            .Setup(x => x.CommitAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dateTimeService
            .Setup(x => x.GetNow())
            .Returns(_now);

        _idempotencyKeyHasher
            .Setup(x => x.Hash(_idempotencyKey))
            .Callback(() => _operationOrder.Add("Hash"))
            .Returns(_keyHash);

        var classRepository = Mock.Get(_unitOfWork.Object.Class);

        classRepository
            .Setup(x => x.GetLockedNoTrackingIgnoringFiltersAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .Callback(() => _operationOrder.Add("Class"))
            .ReturnsAsync(_class);

        classRepository
            .Setup(x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_bookedSeats);

        var waitlistRepository =
            Mock.Get(_unitOfWork.Object.WaitlistEntry);

        waitlistRepository
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .Callback(() => _operationOrder.Add("IdempotencyLookup"))
            .ReturnsAsync((WaitlistEntry?)null);

        waitlistRepository
            .Setup(x => x.GetWaitingCountAndUserWaitingStatusAsync(
                _class.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WaitlistQueueState(
                WaitingCount: _waitingCount,
                HasWaitingForUser: false));

        _classValidationService
            .Setup(x => x.ValidateWaitlist(
                _class,
                _bookedSeats,
                _seats,
                _now))
            .Returns(Result.Success);

        _sut = new JoinWaitlistService(
            _unitOfWork.Object,
            _dateTimeService.Object,
            _idempotencyKeyHasher.Object,
            _classValidationService.Object);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_ValidCommand_CreatesWaitingEntry()
    {
        WaitlistEntry? createdEntry = null;

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.Create(It.IsAny<WaitlistEntry>()))
            .Callback<WaitlistEntry>(entry => createdEntry = entry);

        var command = new JoinWaitlistCommand(
            _class.Id,
            _seats,
            _idempotencyKey,
            _userId);

        var result = await _sut.JoinWaitlistAsync(command);

        Assert.IsFalse(result.IsError);
        Assert.IsNotNull(createdEntry);

        Assert.AreEqual(_userId, createdEntry.UserId);
        Assert.AreEqual(_class.Id, createdEntry.ClassId);
        Assert.AreEqual(_seats, createdEntry.Seats);
        Assert.AreEqual(_expectedPosition, createdEntry.InitialPosition);
        Assert.AreEqual(
            WaitlistEntryStatuses.Waiting,
            createdEntry.Status);
        Assert.AreEqual(_keyHash, createdEntry.IdempotencyKeyHash);
        Assert.AreEqual(_now, createdEntry.CreatedAt);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _classValidationService.Verify(
            x => x.ValidateWaitlist(
                _class,
                _bookedSeats,
                _seats,
                _now),
            Times.Once);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_EmptyQueue_AssignsFirstPosition()
    {
        var emptyQueueCount = 0;
        var expectedFirstPosition = emptyQueueCount + 1;
        WaitlistEntry? createdEntry = null;

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingCountAndUserWaitingStatusAsync(
                _class.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WaitlistQueueState(
                WaitingCount: emptyQueueCount,
                HasWaitingForUser: false));

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.Create(It.IsAny<WaitlistEntry>()))
            .Callback<WaitlistEntry>(entry => createdEntry = entry);

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsFalse(result.IsError);
        Assert.IsNotNull(createdEntry);
        Assert.AreEqual(expectedFirstPosition, createdEntry.InitialPosition);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_ClassNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedNoTrackingIgnoringFiltersAsync(
                _class.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Class?)null);

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(Error.NotFound().Code, result.FirstError.Code);

        _classValidationService.Verify(
            x => x.ValidateWaitlist(
                It.IsAny<Class>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<DateTime>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.GetWaitingCountAndUserWaitingStatusAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.Create(It.IsAny<WaitlistEntry>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_ClassValidationFails_ReturnsError()
    {
        var validationError = Error.Validation(
            "Class.Invalid",
            "The class cannot be joined.");

        _classValidationService
            .Setup(x => x.ValidateWaitlist(
                _class,
                _bookedSeats,
                _seats,
                _now))
            .Returns(validationError);

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(validationError.Code, result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.GetWaitingCountAndUserWaitingStatusAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.Create(It.IsAny<WaitlistEntry>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_UserAlreadyWaiting_ReturnsAlreadyWaiting()
    {
        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingCountAndUserWaitingStatusAsync(
                _class.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WaitlistQueueState(
                WaitingCount: _waitingCount,
                HasWaitingForUser: true));

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            WaitlistErrors.AlreadyWaiting.Code,
            result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.Create(It.IsAny<WaitlistEntry>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_ExistingKeyAndSamePayload_ReturnsOriginalEntry()
    {
        var existingEntry = new WaitlistEntry
        {
            Id = 3,
            UserId = _userId,
            ClassId = _class.Id,
            Seats = _seats,
            InitialPosition = _expectedPosition,
            Status = WaitlistEntryStatuses.Promoted,
            IdempotencyKeyHash = _keyHash,
            CreatedAt = _now.AddDays(-1)
        };

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(_expectedPosition, result.Value.Position);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.Create(It.IsAny<WaitlistEntry>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _classValidationService.Verify(
            x => x.ValidateWaitlist(
                It.IsAny<Class>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<DateTime>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.GetWaitingCountAndUserWaitingStatusAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_ExistingKeyAndDifferentClass_ReturnsPayloadMismatch()
    {
        var existingEntry = new WaitlistEntry
        {
            Id = 3,
            UserId = _userId,
            ClassId = _class.Id + 1,
            Seats = _seats,
            InitialPosition = _expectedPosition,
            Status = WaitlistEntryStatuses.Promoted,
            IdempotencyKeyHash = _keyHash,
            CreatedAt = _now
        };

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            IdempotencyErrors.PayloadMismatch.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            existingEntry.Status);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.Create(It.IsAny<WaitlistEntry>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_ExistingKeyAndDifferentSeats_ReturnsPayloadMismatch()
    {
        var existingEntry = new WaitlistEntry
        {
            Id = 3,
            UserId = _userId,
            ClassId = _class.Id,
            Seats = _seats + 1,
            InitialPosition = _expectedPosition,
            Status = WaitlistEntryStatuses.Promoted,
            IdempotencyKeyHash = _keyHash,
            CreatedAt = _now
        };

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetByUserAndIdempotencyKeyHashAsync(
                _userId,
                _keyHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        var result = await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            IdempotencyErrors.PayloadMismatch.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            existingEntry.Status);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.Create(It.IsAny<WaitlistEntry>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task JoinWaitlistAsync_LocksClassBeforeCheckingIdempotency()
    {
        await _sut.JoinWaitlistAsync(
            new JoinWaitlistCommand(
                _class.Id,
                _seats,
                _idempotencyKey,
                _userId));

        CollectionAssert.AreEqual(
            new[] { "Class", "Hash", "IdempotencyLookup" },
            _operationOrder);
    }
}
