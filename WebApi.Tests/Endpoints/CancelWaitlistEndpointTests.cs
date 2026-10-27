using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class CancelWaitlistEndpointTests
{
    [TestMethod]
    public async Task CancelWaitlist_CancelsOwnedEntryAndReturnsItsId()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await ApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var waitlistId = await JoinWaitlistAsync(client, classId);

        using var response = await ApiTestSupport.CancelWaitlistAsync(client, waitlistId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(waitlistId, body.RootElement.GetProperty("id").GetInt64());
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, body.RootElement.GetProperty("status").GetString());

        var savedEntry = await ApiTestSupport.WithDbAsync(
            db => db.WaitlistEntries.SingleAsync(entry => entry.Id == waitlistId));
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, savedEntry.Status);
    }

    [TestMethod]
    public async Task CancelWaitlist_RepeatedCallReturnsSameCancelledResultWithoutChangingEntry()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await ApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var waitlistId = await JoinWaitlistAsync(client, classId);

        using var firstResponse = await ApiTestSupport.CancelWaitlistAsync(client, waitlistId);
        using var firstBody = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstCreatedAt = await ApiTestSupport.WithDbAsync(
            async db => (await db.WaitlistEntries.SingleAsync(entry => entry.Id == waitlistId)).CreatedAt);

        using var repeatedResponse = await ApiTestSupport.CancelWaitlistAsync(client, waitlistId);

        Assert.AreEqual(HttpStatusCode.OK, repeatedResponse.StatusCode);
        using var repeatedBody = await JsonDocument.ParseAsync(await repeatedResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(firstBody.RootElement.GetProperty("id").GetInt64(), repeatedBody.RootElement.GetProperty("id").GetInt64());
        Assert.AreEqual(firstBody.RootElement.GetProperty("status").GetString(), repeatedBody.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, repeatedBody.RootElement.GetProperty("status").GetString());

        var entries = await ApiTestSupport.WithDbAsync(
            db => db.WaitlistEntries.Where(entry => entry.Id == waitlistId).ToListAsync());
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(WaitlistEntryStatuses.Cancelled, entries[0].Status);
        Assert.AreEqual(firstCreatedAt, entries[0].CreatedAt);
    }

    [TestMethod]
    public async Task CancelWaitlist_NonexistentOrOtherUsersEntryReturnsNotFound()
    {
        var owner = await ApiTestSupport.CreateScenarioAsync();
        var otherUser = await ApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var ownerClient = await ApiTestSupport.CreateAuthenticatedClientAsync(owner.Email);
        using var otherClient = await ApiTestSupport.CreateAuthenticatedClientAsync(otherUser.Email);
        var ownedWaitlistId = await JoinWaitlistAsync(ownerClient, classId);

        using var foreignResponse = await ApiTestSupport.CancelWaitlistAsync(otherClient, ownedWaitlistId);
        using var missingResponse = await ApiTestSupport.CancelWaitlistAsync(otherClient, long.MaxValue);

        Assert.AreEqual(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        var status = await ApiTestSupport.WithDbAsync(
            async db => (await db.WaitlistEntries.SingleAsync(entry => entry.Id == ownedWaitlistId)).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Waiting, status);
    }

    [TestMethod]
    public async Task CancelWaitlist_RemovedUserIsNotPromotedAndRemainingQueueMovesUp()
    {
        var bookingOwner = await ApiTestSupport.CreateScenarioAsync(
            capacity: 1,
            enrollmentSeats: 1);
        var firstWaiter = await ApiTestSupport.CreateScenarioAsync();
        var secondWaiter = await ApiTestSupport.CreateScenarioAsync();
        var thirdWaiter = await ApiTestSupport.CreateScenarioAsync();
        using var bookingOwnerClient = await ApiTestSupport.CreateAuthenticatedClientAsync(bookingOwner.Email);
        using var firstWaiterClient = await ApiTestSupport.CreateAuthenticatedClientAsync(firstWaiter.Email);
        using var secondWaiterClient = await ApiTestSupport.CreateAuthenticatedClientAsync(secondWaiter.Email);
        using var thirdWaiterClient = await ApiTestSupport.CreateAuthenticatedClientAsync(thirdWaiter.Email);

        var firstWaitlistId = await JoinWaitlistAsync(firstWaiterClient, bookingOwner.ClassId);
        var secondWaitlistId = await JoinWaitlistAsync(secondWaiterClient, bookingOwner.ClassId);
        using (var response = await ApiTestSupport.CancelWaitlistAsync(firstWaiterClient, firstWaitlistId))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var thirdJoinResponse = await ApiTestSupport.PostWaitlistAsync(
            thirdWaiterClient,
            bookingOwner.ClassId,
            seats: 1,
            idempotencyKey: Guid.NewGuid().ToString("N"));
        Assert.AreEqual(HttpStatusCode.Created, thirdJoinResponse.StatusCode);
        using var thirdJoinBody = await JsonDocument.ParseAsync(await thirdJoinResponse.Content.ReadAsStreamAsync());
        var thirdWaitlistId = thirdJoinBody.RootElement.GetProperty("id").GetInt64();
        Assert.AreEqual(2, thirdJoinBody.RootElement.GetProperty("position").GetInt32());

        using var cancelBookingResponse = await ApiTestSupport.CancelEnrollmentAsync(
            bookingOwnerClient,
            bookingOwner.EnrollmentId);
        Assert.AreEqual(HttpStatusCode.OK, cancelBookingResponse.StatusCode);

        var state = await ApiTestSupport.WithDbAsync(async db =>
        {
            var entries = await db.WaitlistEntries
                .Where(entry => entry.ClassId == bookingOwner.ClassId)
                .ToListAsync();
            var enrollments = await db.Enrollments
                .Where(enrollment => enrollment.ClassId == bookingOwner.ClassId)
                .ToListAsync();
            return (Entries: entries, Enrollments: enrollments);
        });

        Assert.AreEqual(WaitlistEntryStatuses.Cancelled,
            state.Entries.Single(entry => entry.Id == firstWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Promoted,
            state.Entries.Single(entry => entry.Id == secondWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Waiting,
            state.Entries.Single(entry => entry.Id == thirdWaitlistId).Status);
        Assert.IsFalse(state.Enrollments.Any(enrollment => enrollment.UserId == firstWaiter.UserId && enrollment.Status == EnrollmentStatuses.Active));
        Assert.IsTrue(state.Enrollments.Any(enrollment => enrollment.UserId == secondWaiter.UserId && enrollment.Status == EnrollmentStatuses.Active));
        Assert.IsFalse(state.Enrollments.Any(enrollment => enrollment.UserId == thirdWaiter.UserId && enrollment.Status == EnrollmentStatuses.Active));
    }

    [TestMethod]
    public async Task CancelWaitlist_ExceedingUserRateLimitReturnsTooManyRequests()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var classId = await CreateFullClassAsync();
        using var client = await ApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        var waitlistId = await JoinWaitlistAsync(client, classId);
        HttpResponseMessage? lastResponse = null;

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            lastResponse?.Dispose();
            lastResponse = await ApiTestSupport.CancelWaitlistAsync(client, waitlistId);
            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        Assert.IsNotNull(lastResponse);
        using (lastResponse)
            Assert.AreEqual(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
    }

    private static async Task<long> CreateFullClassAsync()
    {
        var classId = await ApiTestSupport.CreateClassAsync(capacity: 1);
        var bookingOwner = await ApiTestSupport.CreateScenarioAsync();
        await ApiTestSupport.AddEnrollmentAsync(bookingOwner.UserId, classId, seats: 1);
        return classId;
    }

    private static async Task<long> JoinWaitlistAsync(HttpClient client, long classId)
    {
        using var response = await ApiTestSupport.PostWaitlistAsync(
            client,
            classId,
            seats: 1,
            idempotencyKey: Guid.NewGuid().ToString("N"));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetInt64();
    }
}
