using System.Globalization;
using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class ScheduleEndpointTests
{
    [TestMethod]
    public async Task GetSchedule_ReturnsSortedItemsAndCorrectSeatCounts()
    {
        var data = await CreateScheduleDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.User.Email);

        using var response = await EnrollmentApiTestSupport.GetScheduleAsync(
            client,
            EnrollmentApiTestSupport.ScheduleQuery(data.From, data.To));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("continuationToken").ValueKind);

        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(5, items.Length);
        Assert.AreEqual(data.FirstAtFrom.ClassId, items[0].GetProperty("classId").GetInt64());
        Assert.AreEqual(data.SecondAtFrom.ClassId, items[1].GetProperty("classId").GetInt64());
        Assert.IsTrue(items[0].GetProperty("classId").GetInt64() < items[1].GetProperty("classId").GetInt64());
        Assert.AreEqual(data.FirstAtFrom.StartsAt, items[0].GetProperty("startsAt").GetDateTime());

        AssertScheduleItem(items[0], data.FirstAtFrom, freeSeats: 3, mySeats: 3);
        AssertScheduleItem(items[1], data.SecondAtFrom, freeSeats: 0, mySeats: 0);
        AssertScheduleItem(items[2], data.Overbooked, freeSeats: 0, mySeats: 0);
        AssertScheduleItem(items[3], data.OtherRoom, freeSeats: 5, mySeats: 0);
        AssertScheduleItem(items[4], data.Last, freeSeats: 3, mySeats: 0);
    }

    [TestMethod]
    public async Task GetSchedule_FiltersByRoomAndUnknownRoomReturnsEmptyList()
    {
        var data = await CreateScheduleDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.User.Email);

        using var filteredResponse = await EnrollmentApiTestSupport.GetScheduleAsync(
            client,
            EnrollmentApiTestSupport.ScheduleQuery(data.From, data.To, roomId: data.FirstAtFrom.RoomId));
        Assert.AreEqual(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredBody = await JsonDocument.ParseAsync(await filteredResponse.Content.ReadAsStreamAsync());
        var filteredItems = filteredBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(4, filteredItems.Length);
        Assert.IsTrue(filteredItems.All(x => x.GetProperty("room").GetProperty("id").GetInt64() == data.FirstAtFrom.RoomId));

        using var missingRoomResponse = await EnrollmentApiTestSupport.GetScheduleAsync(
            client,
            EnrollmentApiTestSupport.ScheduleQuery(data.From, data.To, roomId: long.MaxValue));
        Assert.AreEqual(HttpStatusCode.OK, missingRoomResponse.StatusCode);
        using var missingRoomBody = await JsonDocument.ParseAsync(await missingRoomResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(0, missingRoomBody.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, missingRoomBody.RootElement.GetProperty("continuationToken").ValueKind);
    }

    [TestMethod]
    public async Task GetSchedule_ContinuationTokenReturnsNonOverlappingPagesInOrder()
    {
        var data = await CreateScheduleDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.User.Email);
        var classIds = new List<long>();
        string? token = null;

        for (var pageNumber = 0; pageNumber < 3; pageNumber++)
        {
            using var response = await EnrollmentApiTestSupport.GetScheduleAsync(
                client,
                EnrollmentApiTestSupport.ScheduleQuery(
                    data.From,
                    data.To,
                    limit: 2,
                    continuationToken: token));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = body.RootElement;
            foreach (var item in root.GetProperty("items").EnumerateArray())
                classIds.Add(item.GetProperty("classId").GetInt64());

            token = root.GetProperty("continuationToken").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("continuationToken").GetString();
            Assert.AreEqual(pageNumber < 2, token is not null);
        }

        CollectionAssert.AreEqual(
            new[]
            {
                data.FirstAtFrom.ClassId,
                data.SecondAtFrom.ClassId,
                data.Overbooked.ClassId,
                data.OtherRoom.ClassId,
                data.Last.ClassId
            },
            classIds.ToArray());
    }

    [DataTestMethod]
    [DataRow("missingFrom")]
    [DataRow("missingTo")]
    [DataRow("invalidFrom")]
    [DataRow("invalidTo")]
    [DataRow("invalidRoom")]
    [DataRow("limitTooSmall")]
    [DataRow("limitTooLarge")]
    [DataRow("invalidToken")]
    [DataRow("samePeriodBounds")]
    [DataRow("reversedPeriod")]
    public async Task GetSchedule_WithInvalidParameters_ReturnsBadRequest(string invalidCase)
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var from = DateTime.UtcNow.AddDays(3);
        var to = from.AddDays(1);
        var fromValue = Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture));
        var toValue = Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture));
        var query = invalidCase switch
        {
            "missingFrom" => $"?to={toValue}",
            "missingTo" => $"?from={fromValue}",
            "invalidFrom" => $"?from=not-a-date&to={toValue}",
            "invalidTo" => $"?from={fromValue}&to=not-a-date",
            "invalidRoom" => $"?from={fromValue}&to={toValue}&roomId=not-a-number",
            "limitTooSmall" => $"?from={fromValue}&to={toValue}&limit=0",
            "limitTooLarge" => $"?from={fromValue}&to={toValue}&limit=101",
            "invalidToken" => $"?from={fromValue}&to={toValue}&continuationToken=not-base64!",
            "samePeriodBounds" => $"?from={fromValue}&to={fromValue}",
            "reversedPeriod" => $"?from={toValue}&to={fromValue}",
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        using var response = await EnrollmentApiTestSupport.GetScheduleAsync(client, query);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, invalidCase);
    }

    [TestMethod]
    public async Task GetSchedule_ExceedingUserRateLimit_ReturnsTooManyRequests()
    {
        var data = await CreateScheduleDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.User.Email);
        var query = EnrollmentApiTestSupport.ScheduleQuery(data.From, data.To);

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            using var response = await EnrollmentApiTestSupport.GetScheduleAsync(client, query);
            var expectedStatus = requestNumber < 60
                ? HttpStatusCode.OK
                : HttpStatusCode.TooManyRequests;
            Assert.AreEqual(expectedStatus, response.StatusCode);
        }
    }

    private static async Task<ScheduleData> CreateScheduleDataAsync()
    {
        var user = await EnrollmentApiTestSupport.CreateScenarioAsync(startsAt: DateTime.UtcNow.AddDays(1));
        var otherUser = await EnrollmentApiTestSupport.CreateScenarioAsync(startsAt: DateTime.UtcNow.AddDays(1));

        var from = TruncateToDatabasePrecision(DateTime.UtcNow.AddDays(100 + user.UserId * 3));
        var to = from.AddDays(2);

        var first = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "First at boundary", from, capacity: 8, roomName: "Studio A");
        var second = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Second at boundary", from, capacity: 4, roomName: "Studio A", roomId: first.RoomId);
        var overbooked = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Overbooked legacy class", from.AddHours(1), capacity: 2, roomName: "Studio A", roomId: first.RoomId);
        var otherRoom = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Other room class", from.AddHours(2), capacity: 5, roomName: "Studio B");
        var last = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Last before exclusive boundary", to.AddHours(-1), capacity: 3, roomName: "Studio A", roomId: first.RoomId);
        var atTo = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "At exclusive boundary", to, capacity: 5, roomName: "Studio A", roomId: first.RoomId);
        var beforeFrom = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Before inclusive boundary", from.AddTicks(-10), capacity: 5, roomName: "Studio A", roomId: first.RoomId);
        var cancelled = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Cancelled class", from.AddHours(3), capacity: 5, roomName: "Studio A",
            isCancelled: true, roomId: first.RoomId);

        await EnrollmentApiTestSupport.AddEnrollmentAsync(user.UserId, first.ClassId, 2);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(user.UserId, first.ClassId, 1);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(user.UserId, first.ClassId, 4, EnrollmentStatuses.Cancelled);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, first.ClassId, 2);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, second.ClassId, 4);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, overbooked.ClassId, 3);

        return new ScheduleData(user, from, to, first, second, overbooked, otherRoom, last,
            atTo, beforeFrom, cancelled);
    }

    private static DateTime TruncateToDatabasePrecision(DateTime value) =>
        new(value.Ticks / 10 * 10, value.Kind);

    private static void AssertScheduleItem(
        JsonElement item,
        ScheduleClassFixture expected,
        int freeSeats,
        int mySeats)
    {
        Assert.AreEqual(expected.ClassId, item.GetProperty("classId").GetInt64());
        Assert.AreEqual(expected.Title, item.GetProperty("title").GetString());
        Assert.AreEqual(expected.StartsAt, item.GetProperty("startsAt").GetDateTime());
        Assert.AreEqual(45, item.GetProperty("durationMinutes").GetInt32());
        Assert.AreEqual(expected.RoomId, item.GetProperty("room").GetProperty("id").GetInt64());
        Assert.AreEqual(expected.RoomName, item.GetProperty("room").GetProperty("name").GetString());
        Assert.AreEqual(expected.Capacity, item.GetProperty("capacity").GetInt32());
        Assert.AreEqual(freeSeats, item.GetProperty("freeSeats").GetInt32());
        Assert.IsTrue(item.GetProperty("freeSeats").GetInt32() >= 0);
        Assert.AreEqual(mySeats, item.GetProperty("mySeats").GetInt32());
    }

    private sealed record ScheduleData(
        EnrollmentScenario User,
        DateTime From,
        DateTime To,
        ScheduleClassFixture FirstAtFrom,
        ScheduleClassFixture SecondAtFrom,
        ScheduleClassFixture Overbooked,
        ScheduleClassFixture OtherRoom,
        ScheduleClassFixture Last,
        ScheduleClassFixture AtTo,
        ScheduleClassFixture BeforeFrom,
        ScheduleClassFixture Cancelled);
}
