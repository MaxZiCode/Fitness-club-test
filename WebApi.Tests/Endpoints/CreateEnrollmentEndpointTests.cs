using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class CreateEnrollmentEndpointTests
{
    [TestMethod]
    public async Task PostEnrollment_CreatesEnrollmentAndConsumesPassVisits()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 5);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            seats: 2,
            idempotencyKey: Guid.NewGuid().ToString("N"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = body.RootElement;
        var enrollmentId = result.GetProperty("id").GetInt64();
        Assert.AreEqual(scenario.ClassId, result.GetProperty("classId").GetInt64());
        Assert.AreEqual(2, result.GetProperty("seats").GetInt32());
        Assert.AreEqual("active", result.GetProperty("status").GetString());
        Assert.IsTrue(result.GetProperty("createdAt").GetDateTime() <= DateTime.UtcNow);

        var savedEnrollment = await EnrollmentApiTestSupport.WithDbAsync(
            async db => await db.Enrollments.FindAsync(enrollmentId));
        var savedPass = await EnrollmentApiTestSupport.WithDbAsync(
            async db => await db.Passes.SingleAsync(x => x.UserId == scenario.UserId));

        Assert.IsNotNull(savedEnrollment);
        Assert.AreEqual(scenario.ClassId, savedEnrollment.ClassId);
        Assert.AreEqual(scenario.UserId, savedEnrollment.UserId);
        Assert.AreEqual(2, savedEnrollment.InitialSeats);
        Assert.AreEqual(3, savedPass.RemainingVisits);
    }

    [TestMethod]
    public async Task PostEnrollment_ReusingKeyAfterEnrollmentChangesReturnsCurrentEnrollmentWithoutChargingAgain()
    {
        const int initialSeats = 2;
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 5);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var firstResponse = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            initialSeats,
            idempotencyKey);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        using var firstBody = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
        var originalId = firstBody.RootElement.GetProperty("id").GetInt64();
        var originalCreatedAt = firstBody.RootElement.GetProperty("createdAt").GetDateTime();
        Assert.AreEqual(initialSeats, firstBody.RootElement.GetProperty("seats").GetInt32());
        Assert.AreEqual(EnrollmentStatuses.Active, firstBody.RootElement.GetProperty("status").GetString());

        await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var enrollment = await db.Enrollments.SingleAsync(x => x.Id == originalId);
            enrollment.Seats = 1;
            enrollment.Status = EnrollmentStatuses.Cancelled;
            enrollment.CancelledAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return true;
        });

        using var replayResponse = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            initialSeats,
            idempotencyKey);

        Assert.AreEqual(HttpStatusCode.Created, replayResponse.StatusCode);
        using var replayBody = await JsonDocument.ParseAsync(await replayResponse.Content.ReadAsStreamAsync());
        var replay = replayBody.RootElement;
        Assert.AreEqual(originalId, replay.GetProperty("id").GetInt64());
        Assert.AreEqual(scenario.ClassId, replay.GetProperty("classId").GetInt64());
        Assert.AreEqual(1, replay.GetProperty("seats").GetInt32());
        Assert.AreEqual(EnrollmentStatuses.Cancelled, replay.GetProperty("status").GetString());
        Assert.AreEqual(originalCreatedAt, replay.GetProperty("createdAt").GetDateTime());

        var state = await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var entries = await db.Enrollments.Where(x => x.UserId == scenario.UserId).ToListAsync();
            var pass = await db.Passes.SingleAsync(x => x.UserId == scenario.UserId);
            return (Entries: entries, RemainingVisits: pass.RemainingVisits);
        });
        Assert.AreEqual(1, state.Entries.Count);
        Assert.AreEqual(originalId, state.Entries[0].Id);
        Assert.AreEqual(initialSeats, state.Entries[0].InitialSeats);
        Assert.AreEqual(3, state.RemainingVisits);
    }

    [TestMethod]
    public async Task PostEnrollment_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var response = await client.PostAsJsonAsync(
            "/enrollments",
            new { classId = scenario.ClassId, seats = 1 });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithoutJwt_ReturnsUnauthorized()
    {
        using var client = EnrollmentApiTestSupport.Factory.CreateClient();
        using var response = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            classId: 1,
            seats: 1,
            idempotencyKey: Guid.NewGuid().ToString("N"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithoutClassId_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments", "{\"seats\":1}", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithoutSeats_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments", "{\"classId\":123}", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task PostEnrollment_WithEmptyOrWhitespaceIdempotencyKey_ReturnsBadRequest(string key)
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments",
            JsonSerializer.Serialize(new { classId = scenario.ClassId, seats = 1 }),
            key);
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithMalformedJson_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments", "{", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithInvalidFieldType_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var request = EnrollmentApiTestSupport.CreateJsonRequest(
            "/enrollments",
            "{\"classId\":\"not-a-number\",\"seats\":1}",
            Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithNonPositiveSeats_ReturnsBadRequest()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var response = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            seats: 0,
            idempotencyKey: Guid.NewGuid().ToString("N"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WithNonexistentClass_ReturnsNotFound()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var response = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            long.MaxValue,
            seats: 1,
            idempotencyKey: Guid.NewGuid().ToString("N"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_WhenPassIsExhausted_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 0);
        await AssertConflictAsync(scenario, EnrollmentErrors.Conflict.PassExhausted);
    }

    [TestMethod]
    public async Task PostEnrollment_WhenPassIsExpired_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(validUntil: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await AssertConflictAsync(scenario, EnrollmentErrors.Conflict.PassExpired);
    }

    [TestMethod]
    public async Task PostEnrollment_WhenClassHasNoCapacity_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(capacity: 1);
        await AssertConflictAsync(scenario, EnrollmentErrors.Conflict.CapacityExceeded, seats: 2);
    }

    [TestMethod]
    public async Task PostEnrollment_ConcurrentBookingsCannotOverdrawOnePassAcrossClasses()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 3, capacity: 10);
        var anotherClassId = await EnrollmentApiTestSupport.CreateClassAsync(capacity: 10);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        var requests = Enumerable.Range(0, 8)
            .Select(index => EnrollmentApiTestSupport.PostEnrollmentAsync(
                client,
                index % 2 == 0 ? scenario.ClassId : anotherClassId,
                seats: 2,
                idempotencyKey: Guid.NewGuid().ToString("N")))
            .ToArray();
        var responses = await Task.WhenAll(requests);

        try
        {
            Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
            Assert.AreEqual(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
            foreach (var response in responses.Where(x => x.StatusCode == HttpStatusCode.Conflict))
            {
                using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                Assert.AreEqual(
                    EnrollmentErrors.Conflict.PassExhausted,
                    body.RootElement.GetProperty("reason").GetString());
            }
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        var state = await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var pass = await db.Passes.SingleAsync(x => x.UserId == scenario.UserId);
            var bookings = await db.Enrollments
                .Where(x => x.UserId == scenario.UserId && x.Status == EnrollmentStatuses.Active)
                .GroupBy(x => x.ClassId)
                .Select(group => new { ClassId = group.Key, Seats = group.Sum(x => x.Seats) })
                .ToListAsync();
            return (pass.RemainingVisits, Bookings: bookings);
        });

        Assert.AreEqual(1, state.RemainingVisits);
        Assert.AreEqual(2, state.Bookings.Sum(x => x.Seats));
        Assert.AreEqual(1, state.Bookings.Count);
        Assert.IsTrue(state.Bookings.All(x => x.Seats <= 10));
    }

    [TestMethod]
    public async Task PostEnrollment_ConcurrentBookingsCannotOverbookOneClassAcrossUsers()
    {
        var first = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 5, capacity: 3);
        var otherUsers = new List<EnrollmentScenario> { first };
        for (var index = 0; index < 7; index++)
            otherUsers.Add(await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 5));

        var clients = new List<HttpClient>();
        try
        {
            foreach (var user in otherUsers)
                clients.Add(await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(user.Email));

            var responses = await Task.WhenAll(clients.Select(client =>
                EnrollmentApiTestSupport.PostEnrollmentAsync(
                    client,
                    first.ClassId,
                    seats: 2,
                    idempotencyKey: Guid.NewGuid().ToString("N"))));

            try
            {
                Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
                Assert.AreEqual(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
                foreach (var response in responses.Where(x => x.StatusCode == HttpStatusCode.Conflict))
                {
                    using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    Assert.AreEqual(
                        EnrollmentErrors.Conflict.CapacityExceeded,
                        body.RootElement.GetProperty("reason").GetString());
                }
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

        var state = await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var userIds = otherUsers.Select(x => x.UserId).ToArray();
            var passes = await db.Passes.Where(x => userIds.Contains(x.UserId)).ToListAsync();
            var bookedSeats = await db.Enrollments
                .Where(x => x.ClassId == first.ClassId && x.Status == EnrollmentStatuses.Active)
                .SumAsync(x => (int?)x.Seats) ?? 0;
            return (Passes: passes, BookedSeats: bookedSeats);
        });

        Assert.IsTrue(state.Passes.All(x => x.RemainingVisits >= 0));
        Assert.AreEqual(2, state.BookedSeats);
        Assert.IsTrue(first.Capacity >= state.BookedSeats);
    }

    [TestMethod]
    public async Task PostEnrollment_WhenClassHasStarted_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(startsAt: DateTime.UtcNow.AddMinutes(-2));
        await AssertConflictAsync(scenario, EnrollmentErrors.Conflict.ClassStarted);
    }

    [TestMethod]
    public async Task PostEnrollment_WhenClassIsCancelled_ReturnsConflictReason()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(isClassCancelled: true);
        await AssertConflictAsync(scenario, EnrollmentErrors.Conflict.ClassCancelled);
    }

    [TestMethod]
    public async Task PostEnrollment_ReusingKeyWithDifferentPayload_ReturnsUnprocessableEntity()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 5);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var key = Guid.NewGuid().ToString("N");

        using var first = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            seats: 1,
            idempotencyKey: key);
        using var second = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            seats: 2,
            idempotencyKey: key);

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [TestMethod]
    public async Task PostEnrollment_ExceedingUserRateLimit_ReturnsTooManyRequests()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: 100);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        HttpResponseMessage? lastResponse = null;

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            lastResponse?.Dispose();
            lastResponse = await EnrollmentApiTestSupport.PostEnrollmentAsync(
                client,
                scenario.ClassId,
                seats: 1,
                idempotencyKey: Guid.NewGuid().ToString("N"));

            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        var response = lastResponse ?? throw new AssertFailedException("No response was received.");
        using (response)
            Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private async Task AssertConflictAsync(EnrollmentScenario scenario, string expectedReason, int seats = 1)
    {
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var response = await EnrollmentApiTestSupport.PostEnrollmentAsync(
            client,
            scenario.ClassId,
            seats,
            idempotencyKey: Guid.NewGuid().ToString("N"));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(expectedReason, body.RootElement.GetProperty("reason").GetString());

        var hasEnrollment = await EnrollmentApiTestSupport.WithDbAsync(
            db => db.Enrollments.AnyAsync(x => x.UserId == scenario.UserId));
        var remainingVisits = await EnrollmentApiTestSupport.WithDbAsync(
            async db => (await db.Passes.SingleAsync(x => x.UserId == scenario.UserId)).RemainingVisits);
        Assert.IsFalse(hasEnrollment);
        Assert.AreEqual(scenario.InitialPassVisits, remainingVisits);
    }
}
