using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Moq;
using ServiceLayer.Enrollments;
using ServiceLayer.Idempotency;
using ServiceLayer.Shared;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class CancelEnrollmentServiceTests
{
    private long _userId;
    private long _waitlistUserId;

    private int _bookedSeats;
    private int _classCapacity;
    private int _initialEnrollmentSeats;
    private int _initialRemainingVisits;
    private int _initialWaitlistRemainingVisits;
    private int _waitlistSeats;
    private int _expectedRemainingVisitsAfterRefund;
    private int _expectedWaitlistRemainingVisitsAfterPromotion;
    private int _expectedRefundedVisits;

    private TimeSpan _refundCutoff;
    private DateTime _now;
    private DateOnly _today;

    private string _promotedIdempotencyKeyHash = null!;

    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IDbContextTransaction> _transaction = null!;

    private Mock<IDateTimeService> _dateTimeService = null!;
    private Mock<IPassValidationService> _passValidationService = null!;
    private Mock<IEnrollmentCardCacheService> _cacheService = null!;
    private Mock<IIdempotencyKeyHasher> _idempotencyKeyHasher = null!;

    private CancelEnrollmentService _sut = null!;

    private Class _class = null!;
    private Enrollment _enrollment = null!;
    private Pass _pass = null!;
    private List<string> _lockOrder = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 1;
        _waitlistUserId = 2;

        _bookedSeats = 3;
        _classCapacity = 8;
        _initialEnrollmentSeats = 2;
        _initialRemainingVisits = 10;
        _initialWaitlistRemainingVisits = 6;
        _waitlistSeats = 2;

        _expectedRefundedVisits = _initialEnrollmentSeats;
        _expectedRemainingVisitsAfterRefund =
            _initialRemainingVisits + _expectedRefundedVisits;
        _expectedWaitlistRemainingVisitsAfterPromotion =
            _initialWaitlistRemainingVisits - _waitlistSeats;

        _refundCutoff = TimeSpan.FromHours(24);
        _now = new DateTime(
            2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        _today = DateOnly.FromDateTime(_now);

        _promotedIdempotencyKeyHash = "promoted-enrollment-key-hash";
        _lockOrder = [];

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _transaction = new Mock<IDbContextTransaction>();

        _dateTimeService = new Mock<IDateTimeService>();
        _passValidationService = new Mock<IPassValidationService>();
        _cacheService = new Mock<IEnrollmentCardCacheService>();
        _idempotencyKeyHasher = new Mock<IIdempotencyKeyHasher>();

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
            Capacity = _classCapacity,
            StartsAt = _now.AddDays(2),
            IsCancelled = false
        };

        _enrollment = new Enrollment
        {
            Id = 3,
            UserId = _userId,
            ClassId = _class.Id,
            Seats = _initialEnrollmentSeats,
            InitialSeats = _initialEnrollmentSeats,
            Status = EnrollmentStatuses.Active,
            CreatedAt = _now.AddDays(-1)
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
            .Setup(x => x.GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
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

        Mock.Get(_unitOfWork.Object.Pass)
            .Setup(x => x.GetLockedByUserIdsAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<long>, CancellationToken>(
                (_, _) => _lockOrder.Add(nameof(Pass)))
            .ReturnsAsync(new List<Pass> { _pass });

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingEntriesForPromotionAsync(
                _class.Id,
                It.IsAny<int>(),
                _userId,
                _enrollment.Seats,
                _today,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WaitlistEntry>());

        _sut = new CancelEnrollmentService(
            _unitOfWork.Object,
            _dateTimeService.Object,
            _passValidationService.Object,
            _cacheService.Object,
            _idempotencyKeyHasher.Object,
            Options.Create(new EnrollmentCancellationOptions
            {
                RefundCutoff = _refundCutoff
            }));
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_ClassNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Class?)null);

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

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
            x => x.GetLockedByUserIdsAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
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
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_EnrollmentNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.GetByIdAndUserAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enrollment?)null);

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            Error.NotFound().Code,
            result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetBookedSeatsAsync(
                _class.Id,
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
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_AlreadyCancelled_ReturnsExistingResult()
    {
        _enrollment.Status = EnrollmentStatuses.Cancelled;

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            _enrollment.Status);

        _dateTimeService.Verify(
            x => x.GetNow(),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_ClassAlreadyStarted_ReturnsError()
    {
        _class.StartsAt = _now.Subtract(TimeSpan.FromMinutes(1));

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ClassErrors.ClassStarted.Code,
            result.FirstError.Code);

        Assert.AreEqual(
            EnrollmentStatuses.Active,
            _enrollment.Status);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
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
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_WithinRefundCutoff_CancelsWithoutRefund()
    {
        _class.StartsAt = _now.Add(_refundCutoff - TimeSpan.FromHours(1));

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            _enrollment.Status);

        Assert.AreEqual(_now, _enrollment.CancelledAt);
        Assert.AreEqual(0, _enrollment.RefundedVisits);

        Assert.AreEqual(
            _initialRemainingVisits,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(ids => !ids.Any()),
                It.IsAny<CancellationToken>()),
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
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_OutsideRefundCutoff_RefundsVisits()
    {
        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            _enrollment.Status);

        Assert.AreEqual(_now, _enrollment.CancelledAt);
        Assert.AreEqual(
            _expectedRefundedVisits,
            _enrollment.RefundedVisits);

        Assert.AreEqual(
            _expectedRemainingVisitsAfterRefund,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.Contains(_userId)),
                It.IsAny<CancellationToken>()),
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
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.Contains(_userId)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_ClassCancelled_RefundsVisitsWithoutCheckingWaitlist()
    {
        _class.IsCancelled = true;

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            _enrollment.Status);

        Assert.AreEqual(
            _expectedRefundedVisits,
            _enrollment.RefundedVisits);

        Assert.AreEqual(
            _expectedRemainingVisitsAfterRefund,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.GetWaitingEntriesForPromotionAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.Contains(_userId)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.Contains(_userId)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_EligibleWaitlistEntry_PromotesEnrollmentAndUpdatesPass()
    {
        var waitingEntry = new WaitlistEntry
        {
            Id = 4,
            UserId = _waitlistUserId,
            ClassId = _class.Id,
            Seats = _waitlistSeats
        };

        var waitingPass = new Pass
        {
            UserId = _waitlistUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        Enrollment? promotedEnrollment = null;

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingEntriesForPromotionAsync(
                _class.Id,
                It.IsAny<int>(),
                _userId,
                _enrollment.Seats,
                _today,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WaitlistEntry> { waitingEntry });

        Mock.Get(_unitOfWork.Object.Pass)
            .Setup(x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.Contains(_userId)
                        && ids.Contains(_waitlistUserId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pass> { _pass, waitingPass });

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.Create(It.IsAny<Enrollment>()))
            .Callback<Enrollment>(enrollment =>
                promotedEnrollment = enrollment);

        _idempotencyKeyHasher
            .Setup(x => x.Hash($"waitlist-promotion:{waitingEntry.Id}"))
            .Returns(_promotedIdempotencyKeyHash);

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            _enrollment.Status);

        Assert.AreEqual(
            _expectedRefundedVisits,
            _enrollment.RefundedVisits);

        Assert.AreEqual(
            _expectedRemainingVisitsAfterRefund,
            _pass.RemainingVisits);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            waitingEntry.Status);

        Assert.AreEqual(
            _expectedWaitlistRemainingVisitsAfterPromotion,
            waitingPass.RemainingVisits);

        Assert.IsNotNull(promotedEnrollment);

        Assert.AreEqual(
            _waitlistUserId,
            promotedEnrollment.UserId);

        Assert.AreEqual(
            _class.Id,
            promotedEnrollment.ClassId);

        Assert.AreEqual(
            _waitlistSeats,
            promotedEnrollment.Seats);

        Assert.AreEqual(
            _waitlistSeats,
            promotedEnrollment.InitialSeats);

        Assert.AreEqual(
            EnrollmentStatuses.Active,
            promotedEnrollment.Status);

        Assert.AreEqual(
            _promotedIdempotencyKeyHash,
            promotedEnrollment.IdempotencyKeyHash);

        Assert.AreEqual(
            _now,
            promotedEnrollment.CreatedAt);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.Create(It.IsAny<Enrollment>()),
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
            x => x.InvalidateEnrollmentsForUsersAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.ToHashSet().SetEquals(
                        new[] { _userId, _waitlistUserId })),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_LocksClassBeforePass()
    {
        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        CollectionAssert.AreEqual(
            new[] { nameof(Class), nameof(Pass) },
            _lockOrder);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_WaitlistPromotion_SkipsEntriesThatDoNotFitAndPromotesLaterEntries()
    {
        var freeSeats = 3;
        _class.Capacity = _bookedSeats - _enrollment.Seats + freeSeats;

        var oversizedSeats = freeSeats + 1;
        var firstPromotedSeats = freeSeats - 1;
        var secondPromotedSeats = freeSeats - firstPromotedSeats;

        var oversizedUserId = _waitlistUserId;
        var firstPromotedUserId = _waitlistUserId + 1;
        var secondPromotedUserId = _waitlistUserId + 2;

        var oversizedEntry = CreateWaitlistEntry(
            4,
            oversizedUserId,
            oversizedSeats);

        var firstPromotedEntry = CreateWaitlistEntry(
            5,
            firstPromotedUserId,
            firstPromotedSeats);

        var secondPromotedEntry = CreateWaitlistEntry(
            6,
            secondPromotedUserId,
            secondPromotedSeats);

        var oversizedPass = new Pass
        {
            UserId = oversizedUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var firstPromotedPass = new Pass
        {
            UserId = firstPromotedUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var secondPromotedPass = new Pass
        {
            UserId = secondPromotedUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var createdEnrollments = new List<Enrollment>();

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingEntriesForPromotionAsync(
                _class.Id,
                freeSeats,
                _userId,
                _enrollment.Seats,
                _today,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WaitlistEntry>
            {
            oversizedEntry,
            firstPromotedEntry,
            secondPromotedEntry
            });

        var expectedPassUserIds = new HashSet<long>
        {
            _userId,
            oversizedUserId,
            firstPromotedUserId,
            secondPromotedUserId
        };

        Mock.Get(_unitOfWork.Object.Pass)
            .Setup(x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => expectedPassUserIds.SetEquals(ids)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pass>
            {
            _pass,
            oversizedPass,
            firstPromotedPass,
            secondPromotedPass
            });

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.Create(It.IsAny<Enrollment>()))
            .Callback<Enrollment>(createdEnrollments.Add);

        _idempotencyKeyHasher
            .Setup(x => x.Hash(It.IsAny<string>()))
            .Returns((string key) => $"hash:{key}");

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            WaitlistEntryStatuses.Waiting,
            oversizedEntry.Status);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            firstPromotedEntry.Status);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            secondPromotedEntry.Status);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits,
            oversizedPass.RemainingVisits);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits - firstPromotedSeats,
            firstPromotedPass.RemainingVisits);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits - secondPromotedSeats,
            secondPromotedPass.RemainingVisits);

        var expectedPromotedUserIds = new[]
        {
            firstPromotedUserId,
            secondPromotedUserId
        };

        CollectionAssert.AreEqual(
            expectedPromotedUserIds,
            createdEnrollments.Select(x => x.UserId).ToArray());

        Assert.AreEqual(
            firstPromotedSeats,
            createdEnrollments.Single(
                x => x.UserId == firstPromotedUserId).Seats);

        Assert.AreEqual(
            secondPromotedSeats,
            createdEnrollments.Single(
                x => x.UserId == secondPromotedUserId).Seats);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.Create(It.IsAny<Enrollment>()),
            Times.Exactly(expectedPromotedUserIds.Length));

        _passValidationService.Verify(
            x => x.ValidateEnrollmentCreate(
                oversizedPass,
                oversizedSeats,
                _today),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_WaitlistPromotion_SkipsEntriesWithInvalidPassAndContinues()
    {
        var freeSeats = 3;
        _class.Capacity = _bookedSeats - _enrollment.Seats + freeSeats;

        var invalidUserId = _waitlistUserId;
        var firstPromotedUserId = _waitlistUserId + 1;
        var secondPromotedUserId = _waitlistUserId + 2;

        var invalidSeats = 2;
        var firstPromotedSeats = 2;
        var secondPromotedSeats = freeSeats - firstPromotedSeats;

        var invalidEntry = CreateWaitlistEntry(
            4,
            invalidUserId,
            invalidSeats);

        var firstPromotedEntry = CreateWaitlistEntry(
            5,
            firstPromotedUserId,
            firstPromotedSeats);

        var secondPromotedEntry = CreateWaitlistEntry(
            6,
            secondPromotedUserId,
            secondPromotedSeats);

        var invalidPass = new Pass
        {
            UserId = invalidUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var firstPromotedPass = new Pass
        {
            UserId = firstPromotedUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var secondPromotedPass = new Pass
        {
            UserId = secondPromotedUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var validationError = Error.Validation(
            "Pass.InsufficientVisits",
            "The pass does not have enough visits.");

        _passValidationService
            .Setup(x => x.ValidateEnrollmentCreate(
                invalidPass,
                invalidSeats,
                _today))
            .Returns(validationError);

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingEntriesForPromotionAsync(
                _class.Id,
                freeSeats,
                _userId,
                _enrollment.Seats,
                _today,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WaitlistEntry>
            {
            invalidEntry,
            firstPromotedEntry,
            secondPromotedEntry
            });

        var expectedPassUserIds = new HashSet<long>
        {
            _userId,
            invalidUserId,
            firstPromotedUserId,
            secondPromotedUserId
        };

        Mock.Get(_unitOfWork.Object.Pass)
            .Setup(x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => expectedPassUserIds.SetEquals(ids)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pass>
            {
            _pass,
            invalidPass,
            firstPromotedPass,
            secondPromotedPass
            });

        var createdEnrollments = new List<Enrollment>();

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.Create(It.IsAny<Enrollment>()))
            .Callback<Enrollment>(createdEnrollments.Add);

        _idempotencyKeyHasher
            .Setup(x => x.Hash(It.IsAny<string>()))
            .Returns((string key) => $"hash:{key}");

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            WaitlistEntryStatuses.Waiting,
            invalidEntry.Status);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            firstPromotedEntry.Status);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            secondPromotedEntry.Status);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits,
            invalidPass.RemainingVisits);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits - firstPromotedSeats,
            firstPromotedPass.RemainingVisits);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits - secondPromotedSeats,
            secondPromotedPass.RemainingVisits);

        var expectedPromotedUserIds = new[]
        {
            firstPromotedUserId,
            secondPromotedUserId
        };

        CollectionAssert.AreEqual(
            expectedPromotedUserIds,
            createdEnrollments.Select(x => x.UserId).ToArray());

        _passValidationService.Verify(
            x => x.ValidateEnrollmentCreate(
                invalidPass,
                invalidSeats,
                _today),
            Times.Once);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.Create(It.IsAny<Enrollment>()),
            Times.Exactly(expectedPromotedUserIds.Length));
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_WaitlistPromotion_StopsWhenAllFreeSeatsAreAllocated()
    {
        var freeSeats = 3;
        _class.Capacity = _bookedSeats - _enrollment.Seats + freeSeats;

        var firstUserId = _waitlistUserId;
        var secondUserId = _waitlistUserId + 1;
        var thirdUserId = _waitlistUserId + 2;

        var firstSeats = 2;
        var secondSeats = freeSeats - firstSeats;
        var thirdSeats = 1;

        var firstEntry = CreateWaitlistEntry(4, firstUserId, firstSeats);
        var secondEntry = CreateWaitlistEntry(5, secondUserId, secondSeats);
        var thirdEntry = CreateWaitlistEntry(6, thirdUserId, thirdSeats);

        var firstPass = new Pass
        {
            UserId = firstUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var secondPass = new Pass
        {
            UserId = secondUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        var thirdPass = new Pass
        {
            UserId = thirdUserId,
            RemainingVisits = _initialWaitlistRemainingVisits
        };

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetWaitingEntriesForPromotionAsync(
                _class.Id,
                freeSeats,
                _userId,
                _enrollment.Seats,
                _today,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WaitlistEntry>
            {
            firstEntry,
            secondEntry,
            thirdEntry
            });

        var expectedPassUserIds = new HashSet<long>
        {
            _userId,
            firstUserId,
            secondUserId,
            thirdUserId
        };

        Mock.Get(_unitOfWork.Object.Pass)
            .Setup(x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => expectedPassUserIds.SetEquals(ids)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pass>
            {
            _pass,
            firstPass,
            secondPass,
            thirdPass
            });

        var createdEnrollments = new List<Enrollment>();

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.Create(It.IsAny<Enrollment>()))
            .Callback<Enrollment>(createdEnrollments.Add);

        _idempotencyKeyHasher
            .Setup(x => x.Hash(It.IsAny<string>()))
            .Returns((string key) => $"hash:{key}");

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            firstEntry.Status);

        Assert.AreEqual(
            WaitlistEntryStatuses.Promoted,
            secondEntry.Status);

        Assert.AreEqual(
            WaitlistEntryStatuses.Waiting,
            thirdEntry.Status);

        var expectedPromotedUserIds = new[]
        {
            firstUserId,
            secondUserId
        };

        CollectionAssert.AreEqual(
            expectedPromotedUserIds,
            createdEnrollments.Select(x => x.UserId).ToArray());

        Assert.AreEqual(
            _initialWaitlistRemainingVisits - firstSeats,
            firstPass.RemainingVisits);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits - secondSeats,
            secondPass.RemainingVisits);

        Assert.AreEqual(
            _initialWaitlistRemainingVisits,
            thirdPass.RemainingVisits);

        _passValidationService.Verify(
            x => x.ValidateEnrollmentCreate(
                thirdPass,
                thirdSeats,
                _today),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.Create(It.IsAny<Enrollment>()),
            Times.Exactly(expectedPromotedUserIds.Length));
    }

    [TestMethod]
    public async Task CancelEnrollmentAsync_WaitlistPromotion_DoesNotQueryWaitlistWhenNoSeatsAreFree()
    {
        var freeSeats = 0;
        _class.Capacity = _bookedSeats - _enrollment.Seats + freeSeats;

        var result = await _sut.CancelEnrollmentAsync(
            new CancelEnrollmentCommand(_enrollment.Id, _userId));

        Assert.IsFalse(result.IsError);

        Assert.AreEqual(
            EnrollmentStatuses.Cancelled,
            _enrollment.Status);

        Assert.AreEqual(
            _expectedRefundedVisits,
            _enrollment.RefundedVisits);

        Assert.AreEqual(
            _expectedRemainingVisitsAfterRefund,
            _pass.RemainingVisits);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetBookedSeatsAsync(
                _class.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.GetWaitingEntriesForPromotionAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetLockedByUserIdsAsync(
                It.Is<IReadOnlyCollection<long>>(
                    ids => ids.Count == 1 && ids.Contains(_userId)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.Create(It.IsAny<Enrollment>()),
            Times.Never);

        _cacheService.Verify(
            x => x.InvalidateEnrollmentForUserAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private WaitlistEntry CreateWaitlistEntry(long id, long userId, int seats)
    {
        return new WaitlistEntry
        {
            Id = id,
            UserId = userId,
            ClassId = _class.Id,
            Seats = seats,
            Status = WaitlistEntryStatuses.Waiting
        };
    }
}
