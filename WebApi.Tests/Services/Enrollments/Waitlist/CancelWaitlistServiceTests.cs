using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using ErrorOr;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ServiceLayer.Enrollments.Waitlist;

namespace WebApi.Tests.Services;

[TestClass]
public sealed class CancelWaitlistServiceTests
{
    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IDbContextTransaction> _transaction = null!;
    private CancelWaitlistService _sut = null!;

    private long _userId;
    private long _entryId;
    private Class _class = null!;
    private WaitlistEntry _entry = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 10;
        _entryId = 20;

        _class = new Class
        {
            Id = 30,
            StartsAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
            Capacity = 10,
            IsCancelled = false
        };

        _entry = new WaitlistEntry
        {
            Id = _entryId,
            UserId = _userId,
            ClassId = _class.Id,
            Seats = 2,
            InitialPosition = 3,
            Status = WaitlistEntryStatuses.Waiting,
            IdempotencyKeyHash = "key-hash",
            CreatedAt = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)
        };

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _transaction = new Mock<IDbContextTransaction>();

        _unitOfWork
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object);

        _transaction
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
                _entryId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_class);

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetByIdAndUserAsync(
                _entryId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_entry);

        _sut = new CancelWaitlistService(_unitOfWork.Object);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_WaitingEntry_CancelsEntryAndSavesChanges()
    {
        var result = await _sut.CancelWaitlistAsync(CreateCommand());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, _entry.Status);
        Assert.AreEqual(_entry.Id, result.Value.Id);
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, result.Value.Status);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_ClassNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
                _entryId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Class?)null);

        var result = await _sut.CancelWaitlistAsync(CreateCommand());

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(Error.NotFound().Code, result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.WaitlistEntry).Verify(
            x => x.GetByIdAndUserAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_EntryNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetByIdAndUserAsync(
                _entryId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntry?)null);

        var result = await _sut.CancelWaitlistAsync(CreateCommand());

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(Error.NotFound().Code, result.FirstError.Code);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_PromotedEntry_ReturnsNotFound()
    {
        _entry.Status = WaitlistEntryStatuses.Promoted;

        var result = await _sut.CancelWaitlistAsync(CreateCommand());

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(Error.NotFound().Code, result.FirstError.Code);

        Assert.AreEqual(WaitlistEntryStatuses.Promoted, _entry.Status);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_AlreadyCancelledEntry_ReturnsEntryWithoutSavingChanges()
    {
        _entry.Status = WaitlistEntryStatuses.Cancelled;

        var result = await _sut.CancelWaitlistAsync(CreateCommand());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, result.Value.Status);

        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _transaction.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_LocksClassBeforeLoadingEntry()
    {
        var operationOrder = new List<string>();

        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
                _entryId,
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => operationOrder.Add("Class"))
            .ReturnsAsync(_class);

        Mock.Get(_unitOfWork.Object.WaitlistEntry)
            .Setup(x => x.GetByIdAndUserAsync(
                _entryId,
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => operationOrder.Add("Entry"))
            .ReturnsAsync(_entry);

        await _sut.CancelWaitlistAsync(CreateCommand());

        CollectionAssert.AreEqual(
            new[] { "Class", "Entry" },
            operationOrder);
    }

    [TestMethod]
    public async Task CancelWaitlistAsync_CommitThrows_PropagatesException()
    {
        var expectedException = new InvalidOperationException();

        _transaction
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.CancelWaitlistAsync(CreateCommand()));

        Assert.AreSame(expectedException, actualException);
    }

    private CancelWaitlistCommand CreateCommand()
    {
        return new CancelWaitlistCommand(_entryId, _userId);
    }
}
