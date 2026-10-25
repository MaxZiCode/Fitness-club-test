using System.Globalization;
using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class AnalyticsLoadEndpointTests
{
    [TestMethod]
    public async Task GetLoad_ReturnsGroupedSortedLoadAndExcludesCancelledData()
    {
        var data = await CreateLoadDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.Admin.Email);

        using var response = await EnrollmentApiTestSupport.GetLoadAsync(
            client,
            LoadQuery(data.From, data.To));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(4, items.Length);

        AssertLoadItem(items[0], data.From, data.RoomAId, "Studio A", classesCount: 2, totalCapacity: 12, bookedSeats: 8);
        AssertLoadItem(items[1], data.From, data.RoomBId, "Studio B", classesCount: 1, totalCapacity: 5, bookedSeats: 2);
        AssertLoadItem(items[2], data.From.AddDays(1), data.RoomAId, "Studio A", classesCount: 1, totalCapacity: 6, bookedSeats: 1);
        AssertLoadItem(items[3], data.To, data.RoomAId, "Studio A", classesCount: 1, totalCapacity: 7, bookedSeats: 2);
    }

    [TestMethod]
    public async Task GetLoad_FiltersByRoomAndUnknownRoomReturnsEmptyList()
    {
        var data = await CreateLoadDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.Admin.Email);

        using var filteredResponse = await EnrollmentApiTestSupport.GetLoadAsync(
            client,
            LoadQuery(data.From, data.To, data.RoomBId));
        Assert.AreEqual(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredBody = await JsonDocument.ParseAsync(await filteredResponse.Content.ReadAsStreamAsync());
        var filteredItems = filteredBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(1, filteredItems.Length);
        AssertLoadItem(filteredItems[0], data.From, data.RoomBId, "Studio B", classesCount: 1, totalCapacity: 5, bookedSeats: 2);

        using var missingRoomResponse = await EnrollmentApiTestSupport.GetLoadAsync(
            client,
            LoadQuery(data.From, data.To, long.MaxValue));
        Assert.AreEqual(HttpStatusCode.OK, missingRoomResponse.StatusCode);
        using var missingRoomBody = await JsonDocument.ParseAsync(await missingRoomResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(0, missingRoomBody.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetLoad_AllowsSingleDatePeriod()
    {
        var data = await CreateLoadDataAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(data.Admin.Email);

        using var response = await EnrollmentApiTestSupport.GetLoadAsync(
            client,
            LoadQuery(data.From, data.From));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(2, items.Length);
        Assert.AreEqual(data.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), items[0].GetProperty("date").GetString());
        Assert.AreEqual(data.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), items[1].GetProperty("date").GetString());
    }

    [TestMethod]
    public async Task GetLoad_NonAdminUserReturnsForbidden()
    {
        var user = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(user.Email);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(150));

        using var response = await EnrollmentApiTestSupport.GetLoadAsync(client, LoadQuery(from, from.AddDays(1)));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("missingFrom")]
    [DataRow("missingTo")]
    [DataRow("invalidFrom")]
    [DataRow("invalidTo")]
    [DataRow("fromHasTime")]
    [DataRow("toHasTime")]
    [DataRow("invalidRoom")]
    [DataRow("reversedPeriod")]
    public async Task GetLoad_WithInvalidParameters_ReturnsBadRequest(string invalidCase)
    {
        var admin = await EnrollmentApiTestSupport.CreateScenarioAsync(role: "admin");
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(admin.Email);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(200));
        var to = from.AddDays(1);
        var fromValue = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toValue = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = invalidCase switch
        {
            "missingFrom" => $"?to={toValue}",
            "missingTo" => $"?from={fromValue}",
            "invalidFrom" => $"?from=not-a-date&to={toValue}",
            "invalidTo" => $"?from={fromValue}&to=not-a-date",
            "fromHasTime" => $"?from={fromValue}T12:00:00Z&to={toValue}",
            "toHasTime" => $"?from={fromValue}&to={toValue}T12:00:00Z",
            "invalidRoom" => $"?from={fromValue}&to={toValue}&roomId=not-a-number",
            "reversedPeriod" => $"?from={toValue}&to={fromValue}",
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        using var response = await EnrollmentApiTestSupport.GetLoadAsync(client, query);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, invalidCase);
    }

    [TestMethod]
    public async Task GetLoad_ExceedingUserRateLimit_ReturnsTooManyRequests()
    {
        var admin = await EnrollmentApiTestSupport.CreateScenarioAsync(role: "admin");
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(admin.Email);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(250));
        var query = LoadQuery(from, from.AddDays(1));

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            using var response = await EnrollmentApiTestSupport.GetLoadAsync(client, query);
            var expectedStatus = requestNumber < 60
                ? HttpStatusCode.OK
                : HttpStatusCode.TooManyRequests;
            Assert.AreEqual(expectedStatus, response.StatusCode);
        }
    }

    private static async Task<LoadData> CreateLoadDataAsync()
    {
        var admin = await EnrollmentApiTestSupport.CreateScenarioAsync(role: "admin");
        var otherUser = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(300 + admin.UserId * 3));
        var to = from.AddDays(2);
        var dayOne = UtcAt(from, 12);
        var dayTwo = UtcAt(from.AddDays(1), 12);
        var dayThree = UtcAt(to, 12);
        var roomA = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Studio A first class", dayOne, 8, "Studio A");
        var roomASameDay = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Studio A second class", dayOne.AddHours(1), 4, "Studio A", roomId: roomA.RoomId);
        var roomB = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Studio B class", dayOne, 5, "Studio B");
        var nextDay = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Studio A next day", dayTwo, 6, "Studio A", roomId: roomA.RoomId);
        var atInclusiveEnd = await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Studio A on final date", dayThree, 7, "Studio A", roomId: roomA.RoomId);
        await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Cancelled class", dayTwo, 100, "Cancelled room", isCancelled: true);
        await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "Before period", UtcAt(from.AddDays(-1), 23), 50, "Outside room");
        await EnrollmentApiTestSupport.CreateScheduleClassAsync(
            "After period", UtcAt(to.AddDays(1), 0), 50, "Outside room");

        await EnrollmentApiTestSupport.AddEnrollmentAsync(admin.UserId, roomA.ClassId, 2);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(admin.UserId, roomA.ClassId, 1);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(admin.UserId, roomA.ClassId, 5, EnrollmentStatuses.Cancelled);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, roomA.ClassId, 2);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, roomASameDay.ClassId, 4, EnrollmentStatuses.Cancelled);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, roomASameDay.ClassId, 3);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, roomB.ClassId, 2);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(admin.UserId, nextDay.ClassId, 1);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, nextDay.ClassId, 4, EnrollmentStatuses.Cancelled);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(otherUser.UserId, atInclusiveEnd.ClassId, 2);

        return new LoadData(admin, from, to, roomA.RoomId, roomB.RoomId);
    }

    private static DateTime UtcAt(DateOnly date, int hour) =>
        new(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);

    private static string LoadQuery(DateOnly from, DateOnly to, long? roomId = null)
    {
        var query = $"?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return roomId.HasValue
            ? $"{query}&roomId={roomId.Value.ToString(CultureInfo.InvariantCulture)}"
            : query;
    }

    private static void AssertLoadItem(
        JsonElement item,
        DateOnly expectedDate,
        long expectedRoomId,
        string expectedRoomName,
        int classesCount,
        int totalCapacity,
        int bookedSeats)
    {
        Assert.AreEqual(expectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item.GetProperty("date").GetString());
        var room = item.GetProperty("room");
        Assert.AreEqual(expectedRoomId, room.GetProperty("id").GetInt64());
        Assert.AreEqual(expectedRoomName, room.GetProperty("name").GetString());
        Assert.AreEqual(classesCount, item.GetProperty("classesCount").GetInt32());
        Assert.AreEqual(totalCapacity, item.GetProperty("totalCapacity").GetInt32());
        Assert.AreEqual(bookedSeats, item.GetProperty("bookedSeats").GetInt32());
    }

    private sealed record LoadData(
        EnrollmentScenario Admin,
        DateOnly From,
        DateOnly To,
        long RoomAId,
        long RoomBId);
}
