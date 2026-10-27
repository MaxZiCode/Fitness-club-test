using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests.Repositories;

[TestClass]
public sealed class ClassRepositoryTests
{
    private const int ActiveSeats = 2;
    private const int OtherUserSeats = 3;
    private const int CancelledSeats = 5;
    private const int AdditionalActiveSeats = 1;
    private const int ClassDurationMinutes = 45;
    private const int RequestedTake = 1;
    private const int FirstCapacity = 8;
    private const int SecondCapacity = 12;
    private const int OtherRoomCapacity = 15;

    private static int _dayIndex;

    private IServiceScope _scope = null!;
    private ClubDbContext _dbContext = null!;
    private ClassRepository _repository = null!;
    private DateTime _day;

    [TestInitialize]
    public void Setup()
    {
        _scope = ApiTestSupport.Factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        _repository = new ClassRepository(_dbContext.Set<Class>());
        _day = DateTime.UtcNow.Date.AddYears(10).AddDays(Interlocked.Increment(ref _dayIndex) * 2);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
    }

    [TestMethod]
    public async Task GetSchedulePageNoTrackingAsync_FiltersDateRangeAndMapsEnrollmentSeats()
    {
        var roomName = $"Room-{Guid.NewGuid():N}";
        var otherRoomName = $"Room-{Guid.NewGuid():N}";

        var atRangeStart = await ApiTestSupport.CreateScheduleClassAsync(
            "AtRangeStart",
            _day,
            FirstCapacity,
            roomName);

        var firstClass = await ApiTestSupport.CreateScheduleClassAsync(
            "First",
            _day.AddHours(10),
            FirstCapacity,
            roomName,
            roomId: atRangeStart.RoomId);

        var secondClass = await ApiTestSupport.CreateScheduleClassAsync(
            "Second",
            _day.AddHours(10),
            SecondCapacity,
            roomName,
            roomId: atRangeStart.RoomId);

        var otherRoomClass = await ApiTestSupport.CreateScheduleClassAsync(
            "OtherRoom",
            _day.AddHours(11),
            OtherRoomCapacity,
            otherRoomName);

        await ApiTestSupport.CreateScheduleClassAsync(
            "AtExclusiveEnd",
            _day.AddDays(1),
            FirstCapacity,
            roomName,
            roomId: atRangeStart.RoomId);

        var firstUser = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddDays(10));
        var otherUser = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddDays(10));

        await ApiTestSupport.AddEnrollmentAsync(
            firstUser.UserId,
            firstClass.ClassId,
            ActiveSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            otherUser.UserId,
            firstClass.ClassId,
            OtherUserSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            firstUser.UserId,
            firstClass.ClassId,
            CancelledSeats,
            EnrollmentStatuses.Cancelled);

        var result = await _repository.GetSchedulePageNoTrackingAsync(
            _day,
            _day.AddDays(1),
            null,
            firstUser.UserId,
            null,
            null,
            10);

        var expected = new[]
        {
            new ScheduleClassRow(
                atRangeStart.ClassId,
                atRangeStart.Title,
                atRangeStart.StartsAt,
                ClassDurationMinutes,
                atRangeStart.RoomId,
                roomName,
                atRangeStart.Capacity,
                0,
                0),
            new ScheduleClassRow(
                firstClass.ClassId,
                firstClass.Title,
                firstClass.StartsAt,
                ClassDurationMinutes,
                firstClass.RoomId,
                roomName,
                firstClass.Capacity,
                ActiveSeats + OtherUserSeats,
                ActiveSeats),
            new ScheduleClassRow(
                secondClass.ClassId,
                secondClass.Title,
                secondClass.StartsAt,
                ClassDurationMinutes,
                secondClass.RoomId,
                roomName,
                secondClass.Capacity,
                0,
                0),
            new ScheduleClassRow(
                otherRoomClass.ClassId,
                otherRoomClass.Title,
                otherRoomClass.StartsAt,
                ClassDurationMinutes,
                otherRoomClass.RoomId,
                otherRoomName,
                otherRoomClass.Capacity,
                0,
                0)
        };

        CollectionAssert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetSchedulePageNoTrackingAsync_AppliesRoomCursorAndTake()
    {
        var roomName = $"Room-{Guid.NewGuid():N}";
        var otherRoomName = $"Room-{Guid.NewGuid():N}";
        var startsAt = _day.AddHours(10);

        var firstClass = await ApiTestSupport.CreateScheduleClassAsync(
            "First",
            startsAt,
            FirstCapacity,
            roomName);

        var secondClass = await ApiTestSupport.CreateScheduleClassAsync(
            "Second",
            startsAt,
            SecondCapacity,
            roomName,
            roomId: firstClass.RoomId);

        await ApiTestSupport.CreateScheduleClassAsync(
            "Third",
            startsAt.AddHours(1),
            OtherRoomCapacity,
            roomName,
            roomId: firstClass.RoomId);

        await ApiTestSupport.CreateScheduleClassAsync(
            "OtherRoom",
            startsAt,
            OtherRoomCapacity,
            otherRoomName);

        var result = await _repository.GetSchedulePageNoTrackingAsync(
            _day,
            _day.AddDays(1),
            firstClass.RoomId,
            userId: 1,
            firstClass.StartsAt,
            firstClass.ClassId,
            RequestedTake);

        var expected = new[]
        {
            new ScheduleClassRow(
                secondClass.ClassId,
                secondClass.Title,
                secondClass.StartsAt,
                ClassDurationMinutes,
                secondClass.RoomId,
                roomName,
                secondClass.Capacity,
                0,
                0)
        };

        CollectionAssert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetLoadSummaryAsync_GroupsByDateAndRoomAndSumsActiveSeats()
    {
        var roomName = $"Room-{Guid.NewGuid():N}";
        var otherRoomName = $"Room-{Guid.NewGuid():N}";

        var firstClass = await ApiTestSupport.CreateScheduleClassAsync(
            "First",
            _day.AddHours(10),
            FirstCapacity,
            roomName);

        var secondClass = await ApiTestSupport.CreateScheduleClassAsync(
            "Second",
            _day.AddHours(12),
            SecondCapacity,
            roomName,
            roomId: firstClass.RoomId);

        var otherRoomClass = await ApiTestSupport.CreateScheduleClassAsync(
            "OtherRoom",
            _day.AddHours(11),
            OtherRoomCapacity,
            otherRoomName);

        await ApiTestSupport.CreateScheduleClassAsync(
            "AtExclusiveEnd",
            _day.AddDays(1),
            FirstCapacity,
            roomName,
            roomId: firstClass.RoomId);

        var firstUser = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddDays(10));
        var otherUser = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddDays(10));

        await ApiTestSupport.AddEnrollmentAsync(
            firstUser.UserId,
            firstClass.ClassId,
            ActiveSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            otherUser.UserId,
            firstClass.ClassId,
            OtherUserSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            firstUser.UserId,
            secondClass.ClassId,
            AdditionalActiveSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            firstUser.UserId,
            firstClass.ClassId,
            CancelledSeats,
            EnrollmentStatuses.Cancelled);

        var result = await _repository.GetLoadSummaryAsync(
            _day,
            _day.AddDays(1),
            null);

        var expected = new[]
        {
            new ClassLoadSummaryRow(
                _day,
                firstClass.RoomId,
                roomName,
                new[] { firstClass, secondClass }.Length,
                firstClass.Capacity + secondClass.Capacity,
                ActiveSeats + OtherUserSeats + AdditionalActiveSeats),
            new ClassLoadSummaryRow(
                _day,
                otherRoomClass.RoomId,
                otherRoomName,
                1,
                otherRoomClass.Capacity,
                0)
        };

        CollectionAssert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetLoadSummaryAsync_RoomFilter_ReturnsOnlyRequestedRoom()
    {
        var roomName = $"Room-{Guid.NewGuid():N}";
        var otherRoomName = $"Room-{Guid.NewGuid():N}";

        var requestedRoomClass = await ApiTestSupport.CreateScheduleClassAsync(
            "RequestedRoom",
            _day.AddHours(10),
            FirstCapacity,
            roomName);

        await ApiTestSupport.CreateScheduleClassAsync(
            "OtherRoom",
            _day.AddHours(11),
            OtherRoomCapacity,
            otherRoomName);

        var result = await _repository.GetLoadSummaryAsync(
            _day,
            _day.AddDays(1),
            requestedRoomClass.RoomId);

        var expected = new[]
        {
            new ClassLoadSummaryRow(
                _day,
                requestedRoomClass.RoomId,
                roomName,
                1,
                requestedRoomClass.Capacity,
                0)
        };

        CollectionAssert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetBookedSeatsAsync_SumsOnlyActiveEnrollments()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddHours(10));
        var otherUser = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddHours(10));

        await ApiTestSupport.AddEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId,
            ActiveSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            otherUser.UserId,
            scenario.ClassId,
            OtherUserSeats);

        await ApiTestSupport.AddEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId,
            CancelledSeats,
            EnrollmentStatuses.Cancelled);

        var result = await _repository.GetBookedSeatsAsync(scenario.ClassId);

        var expected = ActiveSeats + OtherUserSeats;

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetBookedSeatsAsync_ClassDoesNotExist_ReturnsZero()
    {
        var missingClassId = long.MaxValue;

        var result = await _repository.GetBookedSeatsAsync(missingClassId);

        var expected = 0;

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetLockedNoTrackingIgnoringFiltersAsync_CancelledClass_ReturnsClass()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(
            startsAt: _day.AddHours(10),
            isClassCancelled: true);

        var result = await _repository.GetLockedNoTrackingIgnoringFiltersAsync(scenario.ClassId);

        Assert.IsNotNull(result);
        Assert.AreEqual(scenario.ClassId, result.Id);
        Assert.IsTrue(result.IsCancelled);
    }

    [TestMethod]
    public async Task GetLockedNoTrackingIgnoringFiltersAsync_ClassDoesNotExist_ReturnsNull()
    {
        var missingClassId = long.MaxValue;

        var result = await _repository.GetLockedNoTrackingIgnoringFiltersAsync(missingClassId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetLockedByEnrollmentAndUserIgnoringFiltersAsync_MatchingEnrollment_ReturnsClass()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(
            startsAt: _day.AddHours(10),
            isClassCancelled: true,
            enrollmentSeats: ActiveSeats);

        var result = await _repository.GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
            scenario.EnrollmentId,
            scenario.UserId);

        Assert.IsNotNull(result);
        Assert.AreEqual(scenario.ClassId, result.Id);
        Assert.IsTrue(result.IsCancelled);
    }

    [TestMethod]
    public async Task GetLockedByEnrollmentAndUserIgnoringFiltersAsync_WrongUser_ReturnsNull()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(
            startsAt: _day.AddHours(10),
            enrollmentSeats: ActiveSeats);

        var result = await _repository.GetLockedByEnrollmentAndUserIgnoringFiltersAsync(
            scenario.EnrollmentId,
            long.MaxValue);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync_MatchingEnrollment_IncludesRoom()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(
            startsAt: _day.AddHours(10),
            enrollmentSeats: ActiveSeats);

        var result = await _repository.GetLockedByEnrollmentAndUserIgnoringFiltersWithRoomAsync(
            scenario.EnrollmentId,
            scenario.UserId);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Room);
        Assert.AreEqual(scenario.ClassId, result.Id);
    }

    [TestMethod]
    public async Task GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync_MatchingEntry_ReturnsClass()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(
            startsAt: _day.AddHours(10),
            isClassCancelled: true);

        var waitlistEntryId = await CreateWaitlistEntryAsync(scenario.ClassId, scenario.UserId);

        var result = await _repository.GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
            waitlistEntryId,
            scenario.UserId);

        Assert.IsNotNull(result);
        Assert.AreEqual(scenario.ClassId, result.Id);
        Assert.IsTrue(result.IsCancelled);
    }

    [TestMethod]
    public async Task GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync_WrongUser_ReturnsNull()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync(startsAt: _day.AddHours(10));
        var waitlistEntryId = await CreateWaitlistEntryAsync(scenario.ClassId, scenario.UserId);

        var result = await _repository.GetLockedByWaitlistEntryAndUserIgnoringFiltersAsync(
            waitlistEntryId,
            long.MaxValue);

        Assert.IsNull(result);
    }

    private static async Task<long> CreateWaitlistEntryAsync(long classId, long userId)
    {
        var entry = new WaitlistEntry
        {
            ClassId = classId,
            UserId = userId,
            Seats = 1,
            IdempotencyKeyHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            InitialPosition = 1,
            Status = WaitlistEntryStatuses.Waiting
        };

        await ApiTestSupport.WithDbAsync(async db =>
        {
            db.Set<WaitlistEntry>().Add(entry);
            await db.SaveChangesAsync();
            return entry.Id;
        });

        return entry.Id;
    }
}
