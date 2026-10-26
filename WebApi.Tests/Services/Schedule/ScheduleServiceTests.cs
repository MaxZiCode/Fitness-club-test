using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;
using Moq;
using ServiceLayer.Schedule;

namespace WebApi.Tests.Services.Schedule;

[TestClass]
public sealed class ScheduleServiceTests
{
    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IScheduleContinuationTokenService> _continuationTokenService = null!;
    private ScheduleService _sut = null!;

    private DateTime _from;
    private DateTime _to;
    private DateTime _firstStartsAt;
    private DateTime _secondStartsAt;
    private DateTime _thirdStartsAt;

    private long _roomId;
    private long _userId;
    private long _firstClassId;
    private long _secondClassId;
    private long _thirdClassId;

    private int _limit;
    private int _durationMinutes;
    private int _capacity;
    private int _bookedSeats;
    private int _mySeats;
    private int _expectedFreeSeats;
    private int _expectedItemCount;
    private int _expectedTake;

    private string _continuationToken = null!;
    private string _expectedNextToken = null!;
    private string _title = null!;
    private string _roomName = null!;

    private ScheduleCursor _cursor = null!;
    private List<ScheduleClassRow> _rows = null!;

    [TestInitialize]
    public void Setup()
    {
        _from = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
        _to = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        _firstStartsAt = _from.AddHours(10);
        _secondStartsAt = _from.AddDays(1).AddHours(11);
        _thirdStartsAt = _from.AddDays(2).AddHours(12);

        _roomId = 10;
        _userId = 20;

        _firstClassId = 30;
        _secondClassId = 31;
        _thirdClassId = 32;

        _limit = 2;
        _expectedItemCount = _limit;
        _expectedTake = _limit + 1;

        _durationMinutes = 60;
        _capacity = 10;
        _bookedSeats = 7;
        _mySeats = 2;
        _expectedFreeSeats = Math.Max(_capacity - _bookedSeats, 0);

        _continuationToken = "continuation-token";
        _expectedNextToken = "next-token";
        _title = "Morning yoga";
        _roomName = "Room A";

        _cursor = new ScheduleCursor(_secondStartsAt, _secondClassId);
        _rows = [];

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _continuationTokenService =
            new Mock<IScheduleContinuationTokenService>();

        _continuationTokenService
            .Setup(x => x.Decode(_continuationToken))
            .Returns(_cursor);

        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetSchedulePageNoTrackingAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<long>(),
                It.IsAny<DateTime?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rows);

        _sut = new ScheduleService(
            _unitOfWork.Object,
            _continuationTokenService.Object);
    }

    [TestMethod]
    public async Task GetScheduleAsync_WithoutContinuationToken_PassesNullCursor()
    {
        var query = CreateQuery();

        var result = await _sut.GetScheduleAsync(query);

        Assert.IsFalse(result.IsError);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetSchedulePageNoTrackingAsync(
                _from,
                _to,
                _roomId,
                _userId,
                null,
                null,
                _expectedTake,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _continuationTokenService.Verify(
            x => x.Decode(It.IsAny<string>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetScheduleAsync_WithContinuationToken_DecodesAndPassesCursor()
    {
        var query = CreateQuery(_continuationToken);

        var result = await _sut.GetScheduleAsync(query);

        Assert.IsFalse(result.IsError);

        _continuationTokenService.Verify(
            x => x.Decode(_continuationToken),
            Times.Once);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetSchedulePageNoTrackingAsync(
                _from,
                _to,
                _roomId,
                _userId,
                _cursor.StartsAt,
                _cursor.ClassId,
                _expectedTake,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetScheduleAsync_InvalidContinuationToken_ReturnsErrorWithoutQueryingRepository()
    {
        var expectedError = ScheduleErrors.InvalidContinuationToken;

        _continuationTokenService
            .Setup(x => x.Decode(_continuationToken))
            .Returns(expectedError);

        var result = await _sut.GetScheduleAsync(
            CreateQuery(_continuationToken));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(expectedError.Code, result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetSchedulePageNoTrackingAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<long>(),
                It.IsAny<DateTime?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetScheduleAsync_NoRows_ReturnsEmptyPageWithoutContinuationToken()
    {
        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_expectedItemCount - _expectedItemCount, result.Value.Items.Count);
        Assert.IsNull(result.Value.ContinuationToken);

        _continuationTokenService.Verify(
            x => x.Encode(It.IsAny<ScheduleCursor>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetScheduleAsync_FewerRowsThanLimit_ReturnsAllRowsWithoutContinuationToken()
    {
        _rows.Add(CreateRow(
            _firstClassId,
            _title,
            _firstStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_rows.Count, result.Value.Items.Count);
        Assert.IsNull(result.Value.ContinuationToken);
        Assert.AreEqual(_firstClassId, result.Value.Items[0].ClassId);
        Assert.AreEqual(_firstStartsAt, result.Value.Items[0].StartsAt);

        _continuationTokenService.Verify(
            x => x.Encode(It.IsAny<ScheduleCursor>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetScheduleAsync_ExactlyLimitRows_ReturnsAllRowsWithoutContinuationToken()
    {
        _rows.Add(CreateRow(
            _firstClassId,
            _title,
            _firstStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        _rows.Add(CreateRow(
            _secondClassId,
            "Evening yoga",
            _secondStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_expectedItemCount, result.Value.Items.Count);
        Assert.IsNull(result.Value.ContinuationToken);

        _continuationTokenService.Verify(
            x => x.Encode(It.IsAny<ScheduleCursor>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetScheduleAsync_MoreRowsThanLimit_ReturnsLimitItemsAndContinuationToken()
    {
        _rows.Add(CreateRow(
            _firstClassId,
            _title,
            _firstStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        _rows.Add(CreateRow(
            _secondClassId,
            "Evening yoga",
            _secondStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        _rows.Add(CreateRow(
            _thirdClassId,
            "Weekend yoga",
            _thirdStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        var lastReturnedRow = _rows[_limit - 1];
        var expectedCursor = new ScheduleCursor(
            lastReturnedRow.StartsAt,
            lastReturnedRow.ClassId);

        _continuationTokenService
            .Setup(x => x.Encode(expectedCursor))
            .Returns(_expectedNextToken);

        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_expectedItemCount, result.Value.Items.Count);
        Assert.AreEqual(
            _expectedNextToken,
            result.Value.ContinuationToken);

        _continuationTokenService.Verify(
            x => x.Encode(expectedCursor),
            Times.Once);
    }

    [TestMethod]
    public async Task GetScheduleAsync_MoreRowsThanLimit_DoesNotReturnExtraRow()
    {
        _rows.Add(CreateRow(
            _firstClassId,
            _title,
            _firstStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        _rows.Add(CreateRow(
            _secondClassId,
            "Evening yoga",
            _secondStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        _rows.Add(CreateRow(
            _thirdClassId,
            "Weekend yoga",
            _thirdStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats));

        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_expectedItemCount, result.Value.Items.Count);

        CollectionAssert.AreEqual(
            _rows.Take(_limit).Select(x => x.ClassId).ToArray(),
            result.Value.Items.Select(x => x.ClassId).ToArray());
    }

    [TestMethod]
    public async Task GetScheduleAsync_MapsScheduleClassRowToScheduleItem()
    {
        var row = CreateRow(
            _firstClassId,
            _title,
            _firstStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            _bookedSeats,
            _mySeats);

        _rows.Add(row);

        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_firstClassId, result.Value.Items[0].ClassId);
        Assert.AreEqual(_title, result.Value.Items[0].Title);
        Assert.AreEqual(_firstStartsAt, result.Value.Items[0].StartsAt);
        Assert.AreEqual(_durationMinutes, result.Value.Items[0].DurationMinutes);
        Assert.AreEqual(_roomId, result.Value.Items[0].RoomId);
        Assert.AreEqual(_roomName, result.Value.Items[0].RoomName);
        Assert.AreEqual(_capacity, result.Value.Items[0].Capacity);
        Assert.AreEqual(_expectedFreeSeats, result.Value.Items[0].FreeSeats);
        Assert.AreEqual(_mySeats, result.Value.Items[0].MySeats);
    }

    [TestMethod]
    public async Task GetScheduleAsync_BookedSeatsExceedCapacity_MapsFreeSeatsAsZero()
    {
        var bookedSeats = _capacity + _bookedSeats;

        _rows.Add(CreateRow(
            _firstClassId,
            _title,
            _firstStartsAt,
            _durationMinutes,
            _roomId,
            _roomName,
            _capacity,
            bookedSeats,
            _mySeats));

        var result = await _sut.GetScheduleAsync(CreateQuery());

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(
            _expectedItemCount - _expectedItemCount,
            result.Value.Items[0].FreeSeats);
    }

    [TestMethod]
    public async Task GetScheduleAsync_NullRoomId_PassesNullRoomFilter()
    {
        var query = new ScheduleQuery(
            _from,
            _to,
            null,
            _limit,
            null,
            _userId);

        var result = await _sut.GetScheduleAsync(query);

        Assert.IsFalse(result.IsError);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetSchedulePageNoTrackingAsync(
                _from,
                _to,
                null,
                _userId,
                null,
                null,
                _expectedTake,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetScheduleAsync_RepositoryThrows_PropagatesException()
    {
        var expectedException = new InvalidOperationException();

        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetSchedulePageNoTrackingAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<long>(),
                It.IsAny<DateTime?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.GetScheduleAsync(CreateQuery()));

        Assert.AreSame(expectedException, actualException);
    }

    private ScheduleQuery CreateQuery(string? continuationToken = null)
    {
        return new ScheduleQuery(
            _from,
            _to,
            _roomId,
            _limit,
            continuationToken,
            _userId);
    }

    private static ScheduleClassRow CreateRow(
        long classId,
        string title,
        DateTime startsAt,
        int durationMinutes,
        long roomId,
        string roomName,
        int capacity,
        int bookedSeats,
        int mySeats)
    {
        return new ScheduleClassRow(
            classId,
            title,
            startsAt,
            durationMinutes,
            roomId,
            roomName,
            capacity,
            bookedSeats,
            mySeats);
    }
}
