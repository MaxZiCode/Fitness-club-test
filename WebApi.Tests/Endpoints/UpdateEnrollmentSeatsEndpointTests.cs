using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class UpdateEnrollmentSeatsEndpointTests
{
    [TestMethod]
    public async Task UpdateSeats_Increase_ReturnsFullEnrollmentCardAndConsumesDifference()
    {
        var scenario = await CreateBookedScenarioAsync(passVisits: 5, initialSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            scenario.EnrollmentId,
            seats: 4);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = body.RootElement;
        Assert.AreEqual(scenario.EnrollmentId, result.GetProperty("id").GetInt64());
        Assert.AreEqual("active", result.GetProperty("status").GetString());
        Assert.AreEqual(4, result.GetProperty("seats").GetInt32());
        _ = result.GetProperty("createdAt").GetDateTime();
        Assert.AreEqual(scenario.ClassId, result.GetProperty("class").GetProperty("id").GetInt64());
        Assert.AreEqual("Integration test class", result.GetProperty("class").GetProperty("title").GetString());
        _ = result.GetProperty("class").GetProperty("startsAt").GetDateTime();
        Assert.AreEqual(60, result.GetProperty("class").GetProperty("durationMinutes").GetInt32());
        Assert.IsFalse(result.GetProperty("class").GetProperty("isCancelled").GetBoolean());
        Assert.IsTrue(result.GetProperty("class").GetProperty("room").GetProperty("id").GetInt64() > 0);
        Assert.IsTrue(result.GetProperty("class").GetProperty("room").GetProperty("name").GetString()!
            .StartsWith("Test room ", StringComparison.Ordinal));
        Assert.AreEqual(3, result.GetProperty("pass").GetProperty("remainingVisits").GetInt32());
        _ = DateOnly.Parse(result.GetProperty("pass").GetProperty("validUntil").GetString()!);

        var stored = await GetStoredStateAsync(scenario);
        Assert.AreEqual(4, stored.Seats);
        Assert.AreEqual(3, stored.RemainingVisits);
    }

    [TestMethod]
    public async Task UpdateSeats_Decrease_ReturnsDifferenceEvenWhenPassIsExpiredAndExhausted()
    {
        var scenario = await CreateBookedScenarioAsync(
            passVisits: 0,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            initialSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            scenario.EnrollmentId,
            seats: 1);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(1, body.RootElement.GetProperty("seats").GetInt32());
        Assert.AreEqual(1, body.RootElement.GetProperty("pass").GetProperty("remainingVisits").GetInt32());

        var stored = await GetStoredStateAsync(scenario);
        Assert.AreEqual(1, stored.Seats);
        Assert.AreEqual(1, stored.RemainingVisits);
    }

    [TestMethod]
    public async Task UpdateSeats_WithSameNumber_ReturnsCardWithoutChangingState()
    {
        var scenario = await CreateBookedScenarioAsync(
            passVisits: 0,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            initialSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            scenario.EnrollmentId,
            seats: 2);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(2, body.RootElement.GetProperty("seats").GetInt32());
        Assert.AreEqual(0, body.RootElement.GetProperty("pass").GetProperty("remainingVisits").GetInt32());

        var stored = await GetStoredStateAsync(scenario);
        Assert.AreEqual(2, stored.Seats);
        Assert.AreEqual(0, stored.RemainingVisits);
    }

    [TestMethod]
    public async Task UpdateSeats_WithZeroSeats_ReturnsBadRequest()
    {
        var scenario = await CreateBookedScenarioAsync(initialSeats: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            scenario.EnrollmentId,
            seats: 0);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateSeats_WithMissingSeats_ReturnsBadRequest()
    {
        var scenario = await CreateBookedScenarioAsync(initialSeats: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            $"/enrollments/{scenario.EnrollmentId}/seats",
            "{}");
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateSeats_WithMalformedJson_ReturnsBadRequest()
    {
        var scenario = await CreateBookedScenarioAsync(initialSeats: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            $"/enrollments/{scenario.EnrollmentId}/seats",
            "{");
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateSeats_WithInvalidSeatsType_ReturnsBadRequest()
    {
        var scenario = await CreateBookedScenarioAsync(initialSeats: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            $"/enrollments/{scenario.EnrollmentId}/seats",
            "{\"seats\":\"many\"}");
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateSeats_ForMissingEnrollment_ReturnsNotFound()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            long.MaxValue,
            seats: 1);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateSeats_ForAnotherUsersEnrollment_ReturnsNotFound()
    {
        var owner = await CreateBookedScenarioAsync(initialSeats: 1);
        var otherUser = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(otherUser.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            owner.EnrollmentId,
            seats: 2);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var stored = await GetStoredStateAsync(owner);
        Assert.AreEqual(1, stored.Seats);
        Assert.AreEqual(owner.InitialPassVisits, stored.RemainingVisits);
    }

    [TestMethod]
    public async Task UpdateSeats_WhenCapacityIsExceeded_ReturnsConflictReason()
    {
        var scenario = await CreateBookedScenarioAsync(capacity: 1, initialSeats: 1);
        await AssertConflictAsync(scenario, ClassErrors.CapacityExceeded.Code, requestedSeats: 2);
    }

    [TestMethod]
    public async Task UpdateSeats_WhenPassIsExhausted_ReturnsConflictReason()
    {
        var scenario = await CreateBookedScenarioAsync(passVisits: 1, initialSeats: 1);
        await AssertConflictAsync(scenario, PassErrors.PassExhausted.Code, requestedSeats: 3);
    }

    [TestMethod]
    public async Task UpdateSeats_WhenPassIsExpired_ReturnsConflictReason()
    {
        var scenario = await CreateBookedScenarioAsync(
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            initialSeats: 1);
        await AssertConflictAsync(scenario, PassErrors.PassExpired.Code, requestedSeats: 2);
    }

    [TestMethod]
    public async Task UpdateSeats_WhenClassHasStarted_ReturnsConflictReason()
    {
        var scenario = await CreateBookedScenarioAsync(
            startsAt: DateTime.UtcNow.AddMinutes(-2),
            initialSeats: 1);
        await AssertConflictAsync(scenario, ClassErrors.ClassStarted.Code, requestedSeats: 1);
    }

    [TestMethod]
    public async Task UpdateSeats_WhenClassIsCancelled_ReturnsConflictReason()
    {
        var scenario = await CreateBookedScenarioAsync(isClassCancelled: true, initialSeats: 1);
        await AssertConflictAsync(scenario, ClassErrors.ClassCancelled.Code, requestedSeats: 2);
    }

    [TestMethod]
    public async Task UpdateSeats_WhenEnrollmentIsCancelled_ReturnsConflictReason()
    {
        var scenario = await CreateBookedScenarioAsync(
            initialSeats: 1,
            enrollmentStatus: EnrollmentStatuses.Cancelled);
        await AssertConflictAsync(scenario, EnrollmentErrors.EnrollmentCancelled.Code, requestedSeats: 2);
    }

    [TestMethod]
    public async Task UpdateSeats_ExceedingUserRateLimit_ReturnsTooManyRequests()
    {
        var scenario = await CreateBookedScenarioAsync(passVisits: 100, initialSeats: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        HttpResponseMessage? lastResponse = null;

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            lastResponse?.Dispose();
            lastResponse = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
                client,
                scenario.EnrollmentId,
                seats: 1);

            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        var response = lastResponse ?? throw new AssertFailedException("No response was received.");
        using (response)
            Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static async Task AssertConflictAsync(
        EnrollmentScenario scenario,
        string expectedReason,
        int requestedSeats)
    {
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var response = await EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(
            client,
            scenario.EnrollmentId,
            requestedSeats);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(expectedReason, body.RootElement.GetProperty("reason").GetString());

        var stored = await GetStoredStateAsync(scenario);
        Assert.AreEqual(scenario.InitialEnrollmentSeats, stored.Seats);
        Assert.AreEqual(scenario.InitialPassVisits, stored.RemainingVisits);
    }

    private static Task<EnrollmentScenario> CreateBookedScenarioAsync(
        int passVisits = 10,
        DateOnly? validUntil = null,
        int capacity = 10,
        DateTime? startsAt = null,
        bool isClassCancelled = false,
        int initialSeats = 2,
        string enrollmentStatus = EnrollmentStatuses.Active) =>
        EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits,
            validUntil,
            capacity,
            startsAt,
            isClassCancelled,
            initialSeats,
            enrollmentStatus);

    private static Task<StoredEnrollmentState> GetStoredStateAsync(EnrollmentScenario scenario) =>
        EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var enrollment = await db.Enrollments.SingleAsync(x => x.Id == scenario.EnrollmentId);
            var pass = await db.Passes.SingleAsync(x => x.UserId == scenario.UserId);
            return new StoredEnrollmentState(enrollment.Seats, pass.RemainingVisits);
        });

    private sealed record StoredEnrollmentState(int Seats, int RemainingVisits);
}
