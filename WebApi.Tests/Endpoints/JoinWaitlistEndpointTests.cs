using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using ServiceLayer.Enrollments;
using ServiceLayer.Enrollments.Waitlist;
using ServiceLayer.Idempotency;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class JoinWaitlistEndpointTests
{
    [TestMethod]
    public async Task PostWaitlist_CreatesWaitingEntryAndReturnsPosition()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, classId, seats: 2, idempotencyKey: NewKey());

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = body.RootElement;
        var entryId = result.GetProperty("id").GetInt64();
        Assert.IsTrue(entryId > 0);
        Assert.AreEqual(classId, result.GetProperty("classId").GetInt64());
        Assert.AreEqual(2, result.GetProperty("seats").GetInt32());
        Assert.AreEqual(1, result.GetProperty("position").GetInt32());
        Assert.IsTrue(result.GetProperty("createdAt").GetDateTime() <= DateTime.UtcNow);

        var saved = await EnrollmentApiTestSupport.WithDbAsync(
            db => db.WaitlistEntries.SingleAsync(entry => entry.Id == entryId));
        Assert.AreEqual(scenario.UserId, saved.UserId);
        Assert.AreEqual(classId, saved.ClassId);
        Assert.AreEqual(2, saved.Seats);
        Assert.AreEqual(1, saved.InitialPosition);
        Assert.AreEqual(WaitlistEntryStatuses.Waiting, saved.Status);
    }

    [TestMethod]
    public async Task PostWaitlist_ExistingActiveEnrollmentDoesNotPreventWaiting()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync(scenario.UserId);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, classId, seats: 1, idempotencyKey: NewKey());

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(1, body.RootElement.GetProperty("position").GetInt32());
    }

    [TestMethod]
    public async Task PostWaitlist_SecondUserGetsNextPositionAndSameKeyReplaysOriginalEntry()
    {
        var first = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var second = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var firstClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(first.Email);
        using var secondClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(second.Email);
        var firstKey = NewKey();
        var secondKey = NewKey();

        using var firstResponse = await EnrollmentApiTestSupport.PostWaitlistAsync(firstClient, classId, 1, firstKey);
        using var secondResponse = await EnrollmentApiTestSupport.PostWaitlistAsync(secondClient, classId, 3, secondKey);
        using var replayResponse = await EnrollmentApiTestSupport.PostWaitlistAsync(secondClient, classId, 3, secondKey);

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, replayResponse.StatusCode);
        using var firstBody = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
        using var secondBody = await JsonDocument.ParseAsync(await secondResponse.Content.ReadAsStreamAsync());
        using var replayBody = await JsonDocument.ParseAsync(await replayResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(1, firstBody.RootElement.GetProperty("position").GetInt32());
        Assert.AreEqual(2, secondBody.RootElement.GetProperty("position").GetInt32());
        Assert.AreEqual(secondBody.RootElement.GetProperty("id").GetInt64(), replayBody.RootElement.GetProperty("id").GetInt64());
        Assert.AreEqual(2, replayBody.RootElement.GetProperty("position").GetInt32());

        var count = await EnrollmentApiTestSupport.WithDbAsync(
            db => db.WaitlistEntries.CountAsync(entry => entry.ClassId == classId));
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task PostWaitlist_ReusingKeyAfterEntryStatusChangesReturnsSameEntry()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var idempotencyKey = NewKey();

        using var firstResponse = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, classId, seats: 1, idempotencyKey);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        using var firstBody = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
        var entryId = firstBody.RootElement.GetProperty("id").GetInt64();
        var originalCreatedAt = firstBody.RootElement.GetProperty("createdAt").GetDateTime();

        await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var entry = await db.WaitlistEntries.SingleAsync(x => x.Id == entryId);
            entry.Status = WaitlistEntryStatuses.Cancelled;
            await db.SaveChangesAsync();
            return true;
        });

        using var replayResponse = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, classId, seats: 1, idempotencyKey);

        Assert.AreEqual(HttpStatusCode.Created, replayResponse.StatusCode);
        using var replayBody = await JsonDocument.ParseAsync(await replayResponse.Content.ReadAsStreamAsync());
        var replay = replayBody.RootElement;
        Assert.AreEqual(entryId, replay.GetProperty("id").GetInt64());
        Assert.AreEqual(classId, replay.GetProperty("classId").GetInt64());
        Assert.AreEqual(1, replay.GetProperty("seats").GetInt32());
        Assert.AreEqual(1, replay.GetProperty("position").GetInt32());
        Assert.AreEqual(originalCreatedAt, replay.GetProperty("createdAt").GetDateTime());

        var entries = await EnrollmentApiTestSupport.WithDbAsync(
            db => db.WaitlistEntries.Where(x => x.ClassId == classId && x.UserId == scenario.UserId).ToListAsync());
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(1, entries[0].Seats);
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, entries[0].Status);
    }

    [TestMethod]
    public async Task PostWaitlist_WhenSeatsAreAvailable_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(capacity: 4);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        await AssertConflictAsync(client, scenario.ClassId, seats: 1, WaitlistErrors.SeatsAvailable.Code);
    }

    [TestMethod]
    public async Task PostWaitlist_WhenUserIsAlreadyWaiting_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var first = await EnrollmentApiTestSupport.PostWaitlistAsync(client, classId, 1, NewKey());
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        await AssertConflictAsync(client, classId, seats: 2, WaitlistErrors.AlreadyWaiting.Code);
    }

    [TestMethod]
    public async Task PostWaitlist_WhenClassHasStarted_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            startsAt: DateTime.UtcNow.AddMinutes(-1), capacity: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        await AssertConflictAsync(client, scenario.ClassId, seats: 1, ClassErrors.ClassStarted.Code);
    }

    [TestMethod]
    public async Task PostWaitlist_WhenClassIsCancelled_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(isClassCancelled: true, capacity: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        await AssertConflictAsync(client, scenario.ClassId, seats: 1, ClassErrors.ClassCancelled.Code);
    }

    [TestMethod]
    public async Task PostEnrollment_ReusingKeyWithDifferentPayload_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 5);
        var classId = await CreateFullClassAsync(scenario.UserId);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var key = Guid.NewGuid().ToString("N");

        using var first = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client,
            classId,
            seats: 1,
            idempotencyKey: key);
        using var second = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client,
            classId,
            seats: 2,
            idempotencyKey: key);

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        using var secondBody = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
        Assert.AreEqual(IdempotencyErrors.PayloadMismatch.Code, secondBody.RootElement.GetProperty("reason").GetString());
    }

    [TestMethod]
    public async Task PostWaitlist_WhenClassDoesNotExist_ReturnsNotFound()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, long.MaxValue, seats: 1, idempotencyKey: NewKey());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task PostWaitlist_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var response = await client.PostAsJsonAsync(
            "/enrollments/waitlist",
            new { classId = scenario.ClassId, seats = 1 });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task PostWaitlist_WithEmptyOrWhitespaceIdempotencyKey_ReturnsBadRequest(string key)
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments/waitlist", JsonSerializer.Serialize(new { classId = scenario.ClassId, seats = 1 }), key);
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostWaitlist_WithoutClassId_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments/waitlist", "{\"seats\":1}", NewKey());
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostWaitlist_WithoutSeats_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments/waitlist", "{\"classId\":1}", NewKey());
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task PostWaitlist_WithNonPositiveSeats_ReturnsBadRequest(int seats)
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, scenario.ClassId, seats, idempotencyKey: NewKey());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostWaitlist_WithMalformedJsonOrInvalidFieldType_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var malformedRequest = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments/waitlist", "{", NewKey());
        using var malformedResponse = await client.SendAsync(malformedRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, malformedResponse.StatusCode);

        using var invalidTypeRequest = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments/waitlist", "{\"classId\":\"not-a-number\",\"seats\":1}", NewKey());
        using var invalidTypeResponse = await client.SendAsync(invalidTypeRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidTypeResponse.StatusCode);
    }

    [TestMethod]
    public async Task PostWaitlist_ConcurrentRequestsForOneClassGetUniquePositions()
    {
        const int userCount = 8;
        var classId = await CreateFullClassAsync();
        var users = new List<EnrollmentScenario>();
        for (var index = 0; index < userCount; index++)
            users.Add(await EnrollmentApiTestSupport.CreateScenarioAsync());

        var clients = new List<HttpClient>();
        try
        {
            foreach (var user in users)
                clients.Add(await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(user.Email));

            var responses = await Task.WhenAll(clients.Select(client =>
                EnrollmentApiTestSupport.PostWaitlistAsync(client, classId, seats: 1, NewKey())));
            try
            {
                foreach (var response in responses)
                    Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

                var positions = new List<int>();
                foreach (var response in responses)
                {
                    using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    positions.Add(body.RootElement.GetProperty("position").GetInt32());
                }

                CollectionAssert.AreEquivalent(Enumerable.Range(1, userCount).ToArray(), positions.ToArray());
                Assert.AreEqual(userCount, positions.Distinct().Count());
            }
            finally
            {
                foreach (var response in responses)
                    response.Dispose();
            }
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }

        var savedEntries = await EnrollmentApiTestSupport.WithDbAsync(
            db => db.WaitlistEntries
                .Where(entry => entry.ClassId == classId && entry.Status == WaitlistEntryStatuses.Waiting)
                .OrderBy(entry => entry.InitialPosition)
                .Select(entry => entry.InitialPosition)
                .ToListAsync());
        CollectionAssert.AreEqual(Enumerable.Range(1, userCount).ToArray(), savedEntries.ToArray());
    }

    [TestMethod]
    public async Task PostWaitlist_ExceedingUserRateLimit_ReturnsTooManyRequests()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        HttpResponseMessage? lastResponse = null;

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            lastResponse?.Dispose();
            lastResponse = await EnrollmentApiTestSupport.PostWaitlistAsync(client, classId, 1, NewKey());
            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        Assert.IsNotNull(lastResponse);
        using (lastResponse)
            Assert.AreEqual(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
    }

    private static async Task<long> CreateFullClassAsync(long? bookingUserId = null)
    {
        var classId = await EnrollmentApiTestSupport.CreateClassAsync(capacity: 1);
        await EnrollmentApiTestSupport.AddEnrollmentAsync(
            bookingUserId ?? (await EnrollmentApiTestSupport.CreateScenarioAsync()).UserId,
            classId,
            seats: 1);
        return classId;
    }

    private static async Task AssertConflictAsync(
        HttpClient client,
        long classId,
        int seats,
        string expectedReason)
    {
        using var response = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client, classId, seats, idempotencyKey: NewKey());

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(expectedReason, body.RootElement.GetProperty("reason").GetString());
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");
}
