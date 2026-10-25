using System.Globalization;
using System.Net;
using DataAccessLayer;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebApi.Configuration;

namespace WebApi.Tests.Endpoints;

[TestClass]
public sealed class SharedRateLimitEndpointTests
{
    [TestMethod]
    public async Task AllEightEndpoints_ConsumeOneSharedPerUserRateLimit()
    {
        var user = await EnrollmentApiTestSupport.CreateScenarioAsync(
            passVisits: 1000,
            capacity: 1000,
            enrollmentSeats: 1,
            role: "admin");
        var waitlistClassId = await EnrollmentApiTestSupport.CreateClassAsync(capacity: 1);
        var occupancyOwner = await EnrollmentApiTestSupport.CreateScenarioAsync();
        await EnrollmentApiTestSupport.AddEnrollmentAsync(occupancyOwner.UserId, waitlistClassId, seats: 1);
        var waitlistEntryId = await CreateWaitingEntryAsync(user.UserId, waitlistClassId);
        using var client = await EnrollmentApiTestSupport.CreateAuthenticatedClientAsync(user.Email);

        var limit = EnrollmentApiTestSupport.Factory.Services
            .GetRequiredService<IOptions<ApiRateLimitOptions>>()
            .Value.RequestsPerMinute;
        Assert.IsTrue(limit >= 8, "The shared rate-limit test requires a limit of at least eight requests.");

        var from = DateTime.UtcNow.AddDays(-1);
        var to = from.AddDays(30);
        var scheduleQuery = EnrollmentApiTestSupport.ScheduleQuery(from, to);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var loadQuery = $"?from={today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                        $"&to={today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        Func<Task<HttpResponseMessage>>[] endpointRequests =
        [
            () => EnrollmentApiTestSupport.PostEnrollmentAsync(
                client, user.ClassId, seats: 1, idempotencyKey: Guid.NewGuid().ToString("N")),
            () => EnrollmentApiTestSupport.PostEnrollmentSeatsAsync(client, user.EnrollmentId, seats: 1),
            () => EnrollmentApiTestSupport.CancelEnrollmentAsync(client, user.EnrollmentId),
            () => EnrollmentApiTestSupport.GetEnrollmentCardAsync(client, user.EnrollmentId),
            () => EnrollmentApiTestSupport.PostWaitlistAsync(
                client, waitlistClassId, seats: 1, idempotencyKey: Guid.NewGuid().ToString("N")),
            () => EnrollmentApiTestSupport.CancelWaitlistAsync(client, waitlistEntryId),
            () => EnrollmentApiTestSupport.GetScheduleAsync(client, scheduleQuery),
            () => EnrollmentApiTestSupport.GetLoadAsync(client, loadQuery)
        ];

        for (var requestNumber = 0; requestNumber < limit; requestNumber++)
        {
            using var response = await endpointRequests[requestNumber % endpointRequests.Length]();
            Assert.AreNotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode,
                $"Request {requestNumber + 1} of {limit} was rate limited.");
        }

        using var overLimitResponse = await endpointRequests[limit % endpointRequests.Length]();
        Assert.AreEqual(HttpStatusCode.TooManyRequests, overLimitResponse.StatusCode);
    }

    private static Task<long> CreateWaitingEntryAsync(long userId, long classId) =>
        EnrollmentApiTestSupport.WithDbAsync(async db =>
        {
            var entry = new WaitlistEntry
            {
                UserId = userId,
                ClassId = classId,
                Seats = 1,
                InitialPosition = 1,
                Status = WaitlistEntryStatuses.Waiting,
                IdempotencyKeyHash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow
            };
            db.WaitlistEntries.Add(entry);
            await db.SaveChangesAsync();
            return entry.Id;
        });
}
