using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class GetEnrollmentCardEndpointTests
{
    [TestMethod]
    public async Task GetEnrollmentCard_ReturnsCompleteCardForActiveEnrollment()
    {
        var scenario = await CreateCardScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.GetEnrollmentCardAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCard(body.RootElement, scenario, EnrollmentStatuses.Active, classCancelled: false);
    }

    [TestMethod]
    public async Task GetEnrollmentCard_ReturnsCancelledEnrollment()
    {
        var scenario = await CreateCardScenarioAsync(enrollmentStatus: EnrollmentStatuses.Cancelled);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.GetEnrollmentCardAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCard(body.RootElement, scenario, EnrollmentStatuses.Cancelled, classCancelled: false);
    }

    [TestMethod]
    public async Task GetEnrollmentCard_ReturnsEnrollmentForCancelledClass()
    {
        var scenario = await CreateCardScenarioAsync(isClassCancelled: true);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.GetEnrollmentCardAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCard(body.RootElement, scenario, EnrollmentStatuses.Active, classCancelled: true);
    }

    [TestMethod]
    public async Task GetEnrollmentCard_WhenEnrollmentDoesNotExist_ReturnsNotFound()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.GetEnrollmentCardAsync(client, long.MaxValue);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetEnrollmentCard_ForAnotherUser_ReturnsNotFound()
    {
        var owner = await CreateCardScenarioAsync();
        var otherUser = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(otherUser.Email);

        using var response = await EnrollmentApiTestSupport.GetEnrollmentCardAsync(client, owner.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetEnrollmentCard_ExceedingUserRateLimit_ReturnsTooManyRequests()
    {
        var scenario = await CreateCardScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            using var response = await EnrollmentApiTestSupport.GetEnrollmentCardAsync(
                client,
                scenario.EnrollmentId);
            var expectedStatus = requestNumber < 60
                ? HttpStatusCode.OK
                : HttpStatusCode.TooManyRequests;
            Assert.AreEqual(expectedStatus, response.StatusCode);
        }
    }

    private static async Task<EnrollmentScenario> CreateCardScenarioAsync(
        string enrollmentStatus = EnrollmentStatuses.Active,
        bool isClassCancelled = false)
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 7,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40),
            capacity: 12,
            isClassCancelled: isClassCancelled,
            enrollmentSeats: 2,
            enrollmentStatus: enrollmentStatus);
        return scenario;
    }

    private static void AssertCard(
        JsonElement card,
        EnrollmentScenario scenario,
        string expectedStatus,
        bool classCancelled)
    {
        Assert.AreEqual(scenario.EnrollmentId, card.GetProperty("id").GetInt64());
        Assert.AreEqual(expectedStatus, card.GetProperty("status").GetString());
        Assert.AreEqual(scenario.InitialEnrollmentSeats, card.GetProperty("seats").GetInt32());
        _ = card.GetProperty("createdAt").GetDateTime();

        var scheduledClass = card.GetProperty("class");
        Assert.AreEqual(scenario.ClassId, scheduledClass.GetProperty("id").GetInt64());
        Assert.AreEqual("Integration test class", scheduledClass.GetProperty("title").GetString());
        _ = scheduledClass.GetProperty("startsAt").GetDateTime();
        Assert.AreEqual(60, scheduledClass.GetProperty("durationMinutes").GetInt32());
        Assert.AreEqual(classCancelled, scheduledClass.GetProperty("isCancelled").GetBoolean());

        var room = scheduledClass.GetProperty("room");
        Assert.IsTrue(room.GetProperty("id").GetInt64() > 0);
        Assert.IsTrue(room.GetProperty("name").GetString()!.StartsWith("Test room ", StringComparison.Ordinal));

        var pass = card.GetProperty("pass");
        Assert.AreEqual(scenario.InitialPassVisits, pass.GetProperty("remainingVisits").GetInt32());
        Assert.AreEqual(
            scenario.PassValidUntil,
            DateOnly.Parse(pass.GetProperty("validUntil").GetString()!));
    }
}
