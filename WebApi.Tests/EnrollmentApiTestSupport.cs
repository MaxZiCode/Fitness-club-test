using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DataAccessLayer;
using DataAccessLayer.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

internal static class EnrollmentApiTestSupport
{
    private const string TestPassword = "TestPassword123!";

    public static ApiApplicationFactory Factory { get; } = new();

    public static void DisposeFactory() => Factory.Dispose();

    public static async Task<EnrollmentScenario> CreateScenarioAsync(
        int passVisits = 10,
        DateOnly? validUntil = null,
        int capacity = 10,
        DateTime? startsAt = null,
        bool isClassCancelled = false,
        int? enrollmentSeats = null,
        string enrollmentStatus = EnrollmentStatuses.Active,
        string role = "user")
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ClubDbContext>();
        var email = $"test-{Guid.NewGuid():N}@club.test";
        var user = new User
        {
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = services.GetRequiredService<IPasswordHasher<User>>()
            .HashPassword(user, TestPassword);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var passValidUntil = validUntil ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        db.Passes.Add(new Pass
        {
            UserId = user.Id,
            RemainingVisits = passVisits,
            ValidUntil = passValidUntil
        });

        var room = new Room { Name = $"Test room {Guid.NewGuid():N}" };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var scheduledClass = new Class
        {
            RoomId = room.Id,
            Title = "Integration test class",
            StartsAt = startsAt ?? DateTime.UtcNow.AddDays(2),
            DurationMinutes = 60,
            Capacity = capacity,
            IsCancelled = isClassCancelled
        };
        db.Classes.Add(scheduledClass);
        await db.SaveChangesAsync();

        long enrollmentId = 0;
        if (enrollmentSeats is not null)
        {
            var enrollment = new Enrollment
            {
                UserId = user.Id,
                ClassId = scheduledClass.Id,
                Seats = enrollmentSeats.Value,
                InitialSeats = enrollmentSeats.Value,
                Status = enrollmentStatus,
                IdempotencyKeyHash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                CancelledAt = enrollmentStatus == EnrollmentStatuses.Cancelled
                    ? DateTime.UtcNow
                    : null
            };
            db.Enrollments.Add(enrollment);
            await db.SaveChangesAsync();
            enrollmentId = enrollment.Id;
        }

        return new EnrollmentScenario(
            email,
            user.Id,
            scheduledClass.Id,
            enrollmentId,
            capacity,
            passVisits,
            enrollmentSeats,
            passValidUntil);
    }

    public static async Task<long> CreateClassAsync(int capacity = 10, DateTime? startsAt = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var room = new Room { Name = $"Test room {Guid.NewGuid():N}" };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var scheduledClass = new Class
        {
            RoomId = room.Id,
            Title = "Integration test class",
            StartsAt = startsAt ?? DateTime.UtcNow.AddDays(2),
            DurationMinutes = 60,
            Capacity = capacity
        };
        db.Classes.Add(scheduledClass);
        await db.SaveChangesAsync();
        return scheduledClass.Id;
    }

    public static async Task<ScheduleClassFixture> CreateScheduleClassAsync(
        string title,
        DateTime startsAt,
        int capacity,
        string roomName,
        bool isCancelled = false,
        long? roomId = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        if (roomId is null)
        {
            var room = new Room { Name = roomName };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            roomId = room.Id;
        }

        var scheduledClass = new Class
        {
            RoomId = roomId.Value,
            Title = title,
            StartsAt = startsAt,
            DurationMinutes = 45,
            Capacity = capacity,
            IsCancelled = isCancelled
        };
        db.Classes.Add(scheduledClass);
        await db.SaveChangesAsync();

        return new ScheduleClassFixture(
            scheduledClass.Id,
            title,
            startsAt,
            roomId.Value,
            roomName,
            capacity,
            isCancelled);
    }

    public static async Task AddEnrollmentAsync(
        long userId,
        long classId,
        int seats,
        string status = EnrollmentStatuses.Active)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        db.Enrollments.Add(new Enrollment
        {
            UserId = userId,
            ClassId = classId,
            Seats = seats,
            InitialSeats = seats,
            Status = status,
            IdempotencyKeyHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            CancelledAt = status == EnrollmentStatuses.Cancelled ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
    }

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = Factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password = TestPassword });
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpResponseMessage> PostEnrollmentAsync(
        HttpClient client,
        long classId,
        int seats,
        string? idempotencyKey = null)
    {
        var request = CreateJsonRequest(
            "/enrollments",
            JsonSerializer.Serialize(new { classId, seats }),
            idempotencyKey);
        return SendAndDisposeRequestAsync(client, request);
    }

    public static Task<HttpResponseMessage> PostEnrollmentSeatsAsync(
        HttpClient client,
        long enrollmentId,
        int seats)
    {
        var request = CreateJsonRequest(
            $"/enrollments/{enrollmentId}/seats",
            JsonSerializer.Serialize(new { seats }));
        return SendAndDisposeRequestAsync(client, request);
    }

    public static Task<HttpResponseMessage> GetEnrollmentCardAsync(HttpClient client, long enrollmentId) =>
        client.GetAsync($"/enrollments/{enrollmentId}");

    public static Task<HttpResponseMessage> GetScheduleAsync(HttpClient client, string query) =>
        client.GetAsync($"/schedule{query}");

    public static Task<HttpResponseMessage> GetLoadAsync(HttpClient client, string query) =>
        client.GetAsync($"/analytics/load{query}");

    public static string ScheduleQuery(
        DateTime from,
        DateTime to,
        long? roomId = null,
        int? limit = null,
        string? continuationToken = null)
    {
        var parts = new List<string>
        {
            $"from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}",
            $"to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}"
        };
        if (roomId.HasValue)
            parts.Add($"roomId={roomId.Value.ToString(CultureInfo.InvariantCulture)}");
        if (limit.HasValue)
            parts.Add($"limit={limit.Value.ToString(CultureInfo.InvariantCulture)}");
        if (continuationToken is not null)
            parts.Add($"continuationToken={Uri.EscapeDataString(continuationToken)}");

        return $"?{string.Join('&', parts)}";
    }

    public static HttpRequestMessage CreateJsonRequest(string path, string json, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return request;
    }

    public static async Task<T> WithDbAsync<T>(Func<ClubDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ClubDbContext>());
    }

    private static async Task<HttpResponseMessage> SendAndDisposeRequestAsync(
        HttpClient client,
        HttpRequestMessage request)
    {
        using (request)
            return await client.SendAsync(request);
    }
}

internal sealed record EnrollmentScenario(
    string Email,
    long UserId,
    long ClassId,
    long EnrollmentId,
    int Capacity,
    int InitialPassVisits,
    int? InitialEnrollmentSeats,
    DateOnly PassValidUntil);

internal sealed record ScheduleClassFixture(
    long ClassId,
    string Title,
    DateTime StartsAt,
    long RoomId,
    string RoomName,
    int Capacity,
    bool IsCancelled);
