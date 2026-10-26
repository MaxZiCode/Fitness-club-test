using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;
using Moq;
using ServiceLayer.Analytics;

namespace WebApi.Tests.Services.Analytics;

[TestClass]
public sealed class LoadServiceTests
{
    private DateOnly _from;
    private DateOnly _to;
    private DateOnly _expectedNextDay;
    private long _roomId;
    private DateTime _expectedFromUtc;
    private DateTime _expectedToUtcExclusive;
    private List<ClassLoadSummaryRow> _rows = null!;

    private Mock<IUnitOfWork> _unitOfWork = null!;
    private LoadService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _from = new DateOnly(2026, 10, 26);
        _to = new DateOnly(2026, 10, 28);
        _expectedNextDay = _to.AddDays(1);
        _roomId = 42;

        _expectedFromUtc = _from.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Utc);

        _expectedToUtcExclusive = _expectedNextDay.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Utc);

        _rows = [];

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLoadSummaryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_rows);

        _sut = new LoadService(_unitOfWork.Object);
    }

    [TestMethod]
    public async Task GetLoadAsync_ValidQuery_ReturnsEmptyReportWhenNoRowsExist()
    {
        var query = new LoadQuery(_from, _to, _roomId);

        var result = await _sut.GetLoadAsync(query);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(0, result.Value.Items.Count);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetLoadSummaryAsync(
                _expectedFromUtc,
                _expectedToUtcExclusive,
                _roomId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetLoadAsync_ConvertsInclusiveDateRangeToUtcExclusiveRange()
    {
        var query = new LoadQuery(_from, _to, _roomId);

        var result = await _sut.GetLoadAsync(query);

        Assert.IsFalse(result.IsError);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetLoadSummaryAsync(
                It.Is<DateTime>(value =>
                    value == _expectedFromUtc &&
                    value.Kind == DateTimeKind.Utc),
                It.Is<DateTime>(value =>
                    value == _expectedToUtcExclusive &&
                    value.Kind == DateTimeKind.Utc),
                _roomId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetLoadAsync_FromAndToAreSameDay_QueriesOneFullDay()
    {
        var date = _from;
        var nextDay = date.AddDays(1);

        var expectedFromUtc = date.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Utc);

        var expectedToUtcExclusive = nextDay.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Utc);

        var query = new LoadQuery(date, date, _roomId);

        var result = await _sut.GetLoadAsync(query);

        Assert.IsFalse(result.IsError);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetLoadSummaryAsync(
                expectedFromUtc,
                expectedToUtcExclusive,
                _roomId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetLoadAsync_WithoutRoomFilter_PassesNullRoomId()
    {
        long? roomId = null;
        var query = new LoadQuery(_from, _to, roomId);

        var result = await _sut.GetLoadAsync(query);

        Assert.IsFalse(result.IsError);

        Mock.Get(_unitOfWork.Object.Class).Verify(
            x => x.GetLoadSummaryAsync(
                _expectedFromUtc,
                _expectedToUtcExclusive,
                roomId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetLoadAsync_MultipleRows_ReturnsMappedItemsInOriginalOrder()
    {
        var firstDate = _from;
        var secondDate = _from.AddDays(1);

        var firstRoomId = _roomId;
        var secondRoomId = _roomId + 1;

        var firstRoomName = "Room A";
        var secondRoomName = "Room B";

        var firstClassesCount = 3;
        var firstTotalCapacity = 30;
        var firstBookedSeats = 21;

        var secondClassesCount = 2;
        var secondTotalCapacity = 16;
        var secondBookedSeats = 9;

        _rows.Add(new ClassLoadSummaryRow(
            firstDate.ToDateTime(TimeOnly.MinValue),
            firstRoomId,
            firstRoomName,
            firstClassesCount,
            firstTotalCapacity,
            firstBookedSeats));

        _rows.Add(new ClassLoadSummaryRow(
            secondDate.ToDateTime(TimeOnly.MinValue),
            secondRoomId,
            secondRoomName,
            secondClassesCount,
            secondTotalCapacity,
            secondBookedSeats));

        var query = new LoadQuery(_from, _to, _roomId);

        var result = await _sut.GetLoadAsync(query);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_rows.Count, result.Value.Items.Count);

        var firstItem = result.Value.Items[0];

        Assert.AreEqual(firstDate, firstItem.Date);
        Assert.AreEqual(firstRoomId, firstItem.RoomId);
        Assert.AreEqual(firstRoomName, firstItem.RoomName);
        Assert.AreEqual(firstClassesCount, firstItem.ClassesCount);
        Assert.AreEqual(firstTotalCapacity, firstItem.TotalCapacity);
        Assert.AreEqual(firstBookedSeats, firstItem.BookedSeats);

        var secondItem = result.Value.Items[1];

        Assert.AreEqual(secondDate, secondItem.Date);
        Assert.AreEqual(secondRoomId, secondItem.RoomId);
        Assert.AreEqual(secondRoomName, secondItem.RoomName);
        Assert.AreEqual(secondClassesCount, secondItem.ClassesCount);
        Assert.AreEqual(secondTotalCapacity, secondItem.TotalCapacity);
        Assert.AreEqual(secondBookedSeats, secondItem.BookedSeats);
    }

    [TestMethod]
    public async Task GetLoadAsync_RepositoryThrows_PropagatesException()
    {
        var expectedException = new InvalidOperationException();

        Mock.Get(_unitOfWork.Object.Class)
            .Setup(x => x.GetLoadSummaryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var query = new LoadQuery(_from, _to, _roomId);

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.GetLoadAsync(query));

        Assert.AreSame(expectedException, actualException);
    }
}
