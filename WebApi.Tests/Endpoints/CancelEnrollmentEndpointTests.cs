using System.Net;
using System.Text.Json;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class CancelEnrollmentEndpointTests
{
    [TestMethod]
    public async Task CancelEnrollment_BeyondConfiguredRefundCutoffRefundsAllVisitsAndFreesSeats()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 3,
            capacity: 4,
            startsAt: StartsAtBeyondRefundCutoff(),
            enrollmentSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCancelResponse(body.RootElement, scenario.EnrollmentId, refundedVisits: 2);

        var state = await GetStateAsync(scenario);
        Assert.AreEqual(5, state.RemainingVisits);
        Assert.AreEqual(EnrollmentStatuses.Cancelled, state.Enrollments.Single().Status);
        Assert.AreEqual(0, state.ActiveBookedSeats);
    }

    [TestMethod]
    public async Task CancelEnrollment_BeforeConfiguredRefundCutoffFreesSeatsWithoutRefund()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 3,
            capacity: 4,
            startsAt: StartsAtBeforeRefundCutoff(),
            enrollmentSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCancelResponse(body.RootElement, scenario.EnrollmentId, refundedVisits: 0);

        var state = await GetStateAsync(scenario);
        Assert.AreEqual(3, state.RemainingVisits);
        Assert.AreEqual(EnrollmentStatuses.Cancelled, state.Enrollments.Single().Status);
        Assert.AreEqual(0, state.ActiveBookedSeats);
    }

    [TestMethod]
    public async Task CancelEnrollment_CancelledClassAllowsCancellationAfterStartAndRefundsVisits()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 3,
            capacity: 4,
            startsAt: DateTime.UtcNow.AddHours(-1),
            isClassCancelled: true,
            enrollmentSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCancelResponse(body.RootElement, scenario.EnrollmentId, refundedVisits: 2);

        var state = await GetStateAsync(scenario);
        Assert.AreEqual(5, state.RemainingVisits);
        Assert.AreEqual(0, state.ActiveBookedSeats);
    }

    [TestMethod]
    public async Task CancelEnrollment_WhenClassStartedAndNotCancelledReturnsConflict()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 3,
            startsAt: DateTime.UtcNow.AddMinutes(-1),
            enrollmentSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);

        using var response = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.AreEqual(ClassErrors.ClassStarted.Code, body.RootElement.GetProperty("reason").GetString());
        var state = await GetStateAsync(scenario);
        Assert.AreEqual(3, state.RemainingVisits);
        Assert.AreEqual(EnrollmentStatuses.Active, state.Enrollments.Single().Status);
        Assert.AreEqual(2, state.ActiveBookedSeats);
    }

    [TestMethod]
    public async Task CancelEnrollment_RepeatedCallReturnsSameResultWithoutRefundingTwice()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 3,
            startsAt: StartsAtBeyondRefundCutoff(),
            enrollmentSeats: 2);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        using var firstResponse = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstBody = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());

        using var repeatedResponse = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, repeatedResponse.StatusCode);
        using var repeatedBody = await JsonDocument.ParseAsync(await repeatedResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(firstBody.RootElement.GetProperty("id").GetInt64(), repeatedBody.RootElement.GetProperty("id").GetInt64());
        Assert.AreEqual(firstBody.RootElement.GetProperty("status").GetString(), repeatedBody.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(firstBody.RootElement.GetProperty("refundedVisits").GetInt32(), repeatedBody.RootElement.GetProperty("refundedVisits").GetInt32());
        AssertCancelResponse(repeatedBody.RootElement, scenario.EnrollmentId, refundedVisits: 2);

        var state = await GetStateAsync(scenario);
        Assert.AreEqual(5, state.RemainingVisits);
        Assert.AreEqual(1, state.Enrollments.Count);
    }

    [TestMethod]
    public async Task CancelEnrollment_NonexistentOrOtherUsersEnrollmentReturnsNotFound()
    {
        var owner = await EnrollmentApiTestSupport.CreateScenarioAsync(enrollmentSeats: 1);
        var otherUser = await EnrollmentApiTestSupport.CreateScenarioAsync();
        using var ownerClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(owner.Email);
        using var otherClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(otherUser.Email);

        using var foreignResponse = await EnrollmentApiTestSupport.CancelEnrollmentAsync(otherClient, owner.EnrollmentId);
        using var missingResponse = await EnrollmentApiTestSupport.CancelEnrollmentAsync(otherClient, long.MaxValue);

        Assert.AreEqual(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        var state = await GetStateAsync(owner);
        Assert.AreEqual(EnrollmentStatuses.Active, state.Enrollments.Single().Status);
        Assert.AreEqual(owner.InitialPassVisits, state.RemainingVisits);
    }

    [TestMethod]
    public async Task CancelEnrollment_GreedilyPromotesSeveralWaitersIncludingCurrentUser()
    {
        const int capacity = 10;
        const int currentUsersBookingSeats = 8;
        var owner = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 0,
            capacity: capacity,
            startsAt: StartsAtBeyondRefundCutoff(),
            enrollmentSeats: currentUsersBookingSeats);
        var occupancyOwner = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var tooLarge = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var medium = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var skippedForRemainingCapacity = await EnrollmentApiTestSupport.CreateScenarioAsync();
        var finalFit = await EnrollmentApiTestSupport.CreateScenarioAsync();
        await EnrollmentApiTestSupport.AddEnrollmentAsync(occupancyOwner.UserId, owner.ClassId, seats: 2);

        using var ownerClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(owner.Email);
        using var tooLargeClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(tooLarge.Email);
        using var mediumClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(medium.Email);
        using var skippedClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(skippedForRemainingCapacity.Email);
        using var finalFitClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(finalFit.Email);

        var ownerWaitlistId = await JoinWaitlistAsync(ownerClient, owner.ClassId, seats: 2);
        var tooLargeWaitlistId = await JoinWaitlistAsync(tooLargeClient, owner.ClassId, seats: 9);
        var mediumWaitlistId = await JoinWaitlistAsync(mediumClient, owner.ClassId, seats: 4);
        var skippedWaitlistId = await JoinWaitlistAsync(skippedClient, owner.ClassId, seats: 3);
        var finalFitWaitlistId = await JoinWaitlistAsync(finalFitClient, owner.ClassId, seats: 1);

        using var response = await EnrollmentApiTestSupport.CancelEnrollmentAsync(ownerClient, owner.EnrollmentId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        AssertCancelResponse(body.RootElement, owner.EnrollmentId, refundedVisits: currentUsersBookingSeats);

        var state = await GetClassStateAsync(owner.ClassId, owner.UserId);
        Assert.AreEqual(WaitlistEntryStatuses.Promoted, state.WaitlistEntries.Single(x => x.Id == ownerWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Waiting, state.WaitlistEntries.Single(x => x.Id == tooLargeWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Promoted, state.WaitlistEntries.Single(x => x.Id == mediumWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Waiting, state.WaitlistEntries.Single(x => x.Id == skippedWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Promoted, state.WaitlistEntries.Single(x => x.Id == finalFitWaitlistId).Status);

        var promoted = state.Enrollments
            .Where(x => x.Status == EnrollmentStatuses.Active &&
                        x.Id != owner.EnrollmentId &&
                        x.UserId != occupancyOwner.UserId)
            .ToDictionary(x => x.UserId, x => x.Seats);
        Assert.AreEqual(3, promoted.Count);
        Assert.AreEqual(2, promoted[owner.UserId]);
        Assert.AreEqual(4, promoted[medium.UserId]);
        Assert.AreEqual(1, promoted[finalFit.UserId]);
        Assert.IsFalse(promoted.ContainsKey(tooLarge.UserId));
        Assert.IsFalse(promoted.ContainsKey(skippedForRemainingCapacity.UserId));

        Assert.AreEqual(6, state.PassBalances[owner.UserId]);
        Assert.AreEqual(6, state.PassBalances[medium.UserId]);
        Assert.AreEqual(9, state.PassBalances[finalFit.UserId]);
        Assert.AreEqual(skippedForRemainingCapacity.InitialPassVisits, state.PassBalances[skippedForRemainingCapacity.UserId]);
        Assert.AreEqual(tooLarge.InitialPassVisits, state.PassBalances[tooLarge.UserId]);
        Assert.AreEqual(9, state.ActiveBookedSeats);
        Assert.AreEqual(EnrollmentStatuses.Cancelled,
            state.Enrollments.Single(x => x.Id == owner.EnrollmentId).Status);

        foreach (var promotedEnrollment in state.Enrollments.Where(
                     x => x.Status == EnrollmentStatuses.Active && x.Id != owner.EnrollmentId && x.UserId != occupancyOwner.UserId))
        {
            Assert.AreEqual(promotedEnrollment.Seats, promotedEnrollment.InitialSeats);
            Assert.IsFalse(string.IsNullOrWhiteSpace(promotedEnrollment.IdempotencyKeyHash));
        }
    }

    [TestMethod]
    public async Task CancelEnrollment_ConcurrentCancellationsForSameClassPromoteWaitersWithoutOverbooking()
    {
        const int seatsPerEnrollment = 5;
        var firstOwner = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: seatsPerEnrollment,
            capacity: seatsPerEnrollment * 2,
            startsAt: DateTime.UtcNow.AddDays(1),
            enrollmentSeats: seatsPerEnrollment);
        var secondOwner = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: seatsPerEnrollment);
        var secondEnrollmentId = await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var enrollment = new Enrollment
            {
                UserId = secondOwner.UserId,
                ClassId = firstOwner.ClassId,
                Seats = seatsPerEnrollment,
                InitialSeats = seatsPerEnrollment,
                Status = EnrollmentStatuses.Active,
                IdempotencyKeyHash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow
            };
            db.Enrollments.Add(enrollment);
            await db.SaveChangesAsync();
            return enrollment.Id;
        });

        var firstWaiter = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: seatsPerEnrollment);
        var secondWaiter = await EnrollmentApiTestSupport.CreateScenarioAsync(passVisits: seatsPerEnrollment);
        using var firstOwnerClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(firstOwner.Email);
        using var secondOwnerClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(secondOwner.Email);
        using var firstWaiterClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(firstWaiter.Email);
        using var secondWaiterClient = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(secondWaiter.Email);

        var firstWaitlistId = await JoinWaitlistAsync(firstWaiterClient, firstOwner.ClassId, seatsPerEnrollment);
        var secondWaitlistId = await JoinWaitlistAsync(secondWaiterClient, firstOwner.ClassId, seatsPerEnrollment);

        var cancellationResponses = await Task.WhenAll(
            EnrollmentApiTestSupport.CancelEnrollmentAsync(firstOwnerClient, firstOwner.EnrollmentId),
            EnrollmentApiTestSupport.CancelEnrollmentAsync(secondOwnerClient, secondEnrollmentId));
        try
        {
            foreach (var response in cancellationResponses)
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                Assert.AreEqual(seatsPerEnrollment, body.RootElement.GetProperty("refundedVisits").GetInt32());
            }
        }
        finally
        {
            foreach (var response in cancellationResponses)
                response.Dispose();
        }

        var state = await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var enrollments = await db.Enrollments
                .Where(enrollment => enrollment.ClassId == firstOwner.ClassId)
                .ToListAsync();
            var waitlistEntries = await db.WaitlistEntries
                .Where(entry => entry.ClassId == firstOwner.ClassId)
                .ToListAsync();
            var userIds = new[] { firstOwner.UserId, secondOwner.UserId, firstWaiter.UserId, secondWaiter.UserId };
            var passBalances = await db.Passes
                .Where(pass => userIds.Contains(pass.UserId))
                .ToDictionaryAsync(pass => pass.UserId, pass => pass.RemainingVisits);
            return (Enrollments: enrollments, WaitlistEntries: waitlistEntries, PassBalances: passBalances);
        });

        Assert.AreEqual(EnrollmentStatuses.Cancelled,
            state.Enrollments.Single(enrollment => enrollment.Id == firstOwner.EnrollmentId).Status);
        Assert.AreEqual(EnrollmentStatuses.Cancelled,
            state.Enrollments.Single(enrollment => enrollment.Id == secondEnrollmentId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Promoted,
            state.WaitlistEntries.Single(entry => entry.Id == firstWaitlistId).Status);
        Assert.AreEqual(WaitlistEntryStatuses.Promoted,
            state.WaitlistEntries.Single(entry => entry.Id == secondWaitlistId).Status);

        var promoted = state.Enrollments
            .Where(enrollment => enrollment.Status == EnrollmentStatuses.Active)
            .ToList();
        Assert.AreEqual(2, promoted.Count);
        Assert.IsTrue(promoted.All(enrollment => enrollment.Seats == seatsPerEnrollment));
        Assert.IsTrue(promoted.All(enrollment => enrollment.InitialSeats == seatsPerEnrollment));
        Assert.IsTrue(promoted.All(enrollment => !string.IsNullOrWhiteSpace(enrollment.IdempotencyKeyHash)));
        CollectionAssert.AreEquivalent(
            new[] { firstWaiter.UserId, secondWaiter.UserId },
            promoted.Select(enrollment => enrollment.UserId).ToArray());
        Assert.AreEqual(firstOwner.Capacity, promoted.Sum(enrollment => enrollment.Seats));

        Assert.AreEqual(seatsPerEnrollment * 2, state.PassBalances[firstOwner.UserId]);
        Assert.AreEqual(seatsPerEnrollment * 2, state.PassBalances[secondOwner.UserId]);
        Assert.AreEqual(0, state.PassBalances[firstWaiter.UserId]);
        Assert.AreEqual(0, state.PassBalances[secondWaiter.UserId]);
    }

    [TestMethod]
    public async Task CancelEnrollment_ExceedingUserRateLimitReturnsTooManyRequests()
    {
        var scenario = await EnrollmentApiTestSupport.CreateScenarioAsync(enrollmentSeats: 1);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(scenario.Email);
        HttpResponseMessage? lastResponse = null;

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            lastResponse?.Dispose();
            lastResponse = await EnrollmentApiTestSupport.CancelEnrollmentAsync(client, scenario.EnrollmentId);
            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        Assert.IsNotNull(lastResponse);
        using (lastResponse)
            Assert.AreEqual(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
    }

    private static async Task<long> JoinWaitlistAsync(HttpClient client, long classId, int seats)
    {
        using var response = await EnrollmentApiTestSupport.PostWaitlistAsync(
            client,
            classId,
            seats,
            Guid.NewGuid().ToString("N"));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetInt64();
    }

    private static DateTime StartsAtBeyondRefundCutoff() =>
        DateTime.UtcNow.Add(RefundCutoff + CutoffMargin);

    private static DateTime StartsAtBeforeRefundCutoff() =>
        DateTime.UtcNow.Add(RefundCutoff - CutoffMargin);

    private static TimeSpan RefundCutoff =>
        EnrollmentApiTestSupport.Factory.Services
            .GetRequiredService<IOptions<EnrollmentCancellationOptions>>()
            .Value.RefundCutoff;

    private static TimeSpan CutoffMargin
    {
        get
        {
            var cutoff = RefundCutoff;
            Assert.IsTrue(cutoff > TimeSpan.Zero, "RefundCutoff must be positive for boundary tests.");
            return TimeSpan.FromTicks(Math.Max(1, cutoff.Ticks / 10));
        }
    }

    private static async Task<EnrollmentState> GetStateAsync(EnrollmentScenario scenario) =>
        await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var enrollments = await db.Enrollments
                .Where(enrollment => enrollment.UserId == scenario.UserId && enrollment.ClassId == scenario.ClassId)
                .ToListAsync();
            var pass = await db.Passes.SingleAsync(x => x.UserId == scenario.UserId);
            var bookedSeats = await db.Enrollments
                .Where(enrollment => enrollment.ClassId == scenario.ClassId && enrollment.Status == EnrollmentStatuses.Active)
                .SumAsync(enrollment => (int?)enrollment.Seats) ?? 0;
            return new EnrollmentState(enrollments, pass.RemainingVisits, bookedSeats);
        });

    private static async Task<ClassState> GetClassStateAsync(long classId, long ownerUserId) =>
        await EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var waitlistEntries = await db.WaitlistEntries
                .Where(entry => entry.ClassId == classId)
                .ToListAsync();
            var enrollments = await db.Enrollments
                .Where(enrollment => enrollment.ClassId == classId)
                .ToListAsync();
            var userIds = waitlistEntries.Select(entry => entry.UserId)
                .Append(ownerUserId)
                .Distinct()
                .ToArray();
            var passes = await db.Passes
                .Where(pass => userIds.Contains(pass.UserId))
                .ToDictionaryAsync(pass => pass.UserId, pass => pass.RemainingVisits);
            var activeBookedSeats = enrollments
                .Where(enrollment => enrollment.Status == EnrollmentStatuses.Active)
                .Sum(enrollment => enrollment.Seats);
            return new ClassState(waitlistEntries, enrollments, passes, activeBookedSeats);
        });

    private static void AssertCancelResponse(JsonElement response, long enrollmentId, int refundedVisits)
    {
        Assert.AreEqual(enrollmentId, response.GetProperty("id").GetInt64());
        Assert.AreEqual(EnrollmentStatuses.Cancelled, response.GetProperty("status").GetString());
        Assert.AreEqual(refundedVisits, response.GetProperty("refundedVisits").GetInt32());
    }

    private sealed record EnrollmentState(
        IReadOnlyList<Enrollment> Enrollments,
        int RemainingVisits,
        int ActiveBookedSeats);

    private sealed record ClassState(
        IReadOnlyList<WaitlistEntry> WaitlistEntries,
        IReadOnlyList<Enrollment> Enrollments,
        IReadOnlyDictionary<long, int> PassBalances,
        int ActiveBookedSeats);
}
