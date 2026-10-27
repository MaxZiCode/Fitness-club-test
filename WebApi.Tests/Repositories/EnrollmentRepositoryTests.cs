using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests.Repositories;

[TestClass]
public class EnrollmentRepositoryTests
{
    private const int Seats = 2;
    private const long WrongUserId = long.MaxValue - 1;

    private IServiceScope _scope = null!;
    private ClubDbContext _dbContext = null!;
    private EnrollmentRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _scope = ApiTestSupport.Factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        _repository = new EnrollmentRepository(_dbContext.Set<Enrollment>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
    }

    [TestMethod]
    public async Task GetByIdAndUserWithClassAndRoomNoTrackingAsync_ReturnsEnrollmentWithClassAndRoom()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var enrollment = await CreateEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId,
            EnrollmentStatuses.Cancelled);

        var result = await _repository.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
            enrollment.Id,
            scenario.UserId);

        Assert.IsNotNull(result);
        Assert.AreEqual(enrollment.Id, result.Id);
        Assert.AreEqual(scenario.UserId, result.UserId);
        Assert.AreEqual(scenario.ClassId, result.ClassId);
        Assert.AreEqual(EnrollmentStatuses.Cancelled, result.Status);

        Assert.IsNotNull(result.Class);
        Assert.AreEqual(scenario.ClassId, result.Class.Id);

        Assert.IsNotNull(result.Class.Room);
        Assert.AreEqual(result.Class.RoomId, result.Class.Room.Id);

        Assert.AreEqual(EntityState.Detached, _dbContext.Entry(result).State);
    }

    [TestMethod]
    public async Task GetByIdAndUserAsync_ExistingEnrollment_ReturnsTrackedEnrollment()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var enrollment = await CreateEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId);

        var result = await _repository.GetByIdAndUserAsync(
            enrollment.Id,
            scenario.UserId);

        Assert.IsNotNull(result);
        Assert.AreEqual(enrollment.Id, result.Id);
        Assert.AreEqual(scenario.UserId, result.UserId);
        Assert.AreEqual(scenario.ClassId, result.ClassId);
        Assert.AreEqual(EntityState.Unchanged, _dbContext.Entry(result).State);
    }

    [TestMethod]
    public async Task GetByIdAndUserAsync_WrongUser_ReturnsNull()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var enrollment = await CreateEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId);

        var result = await _repository.GetByIdAndUserAsync(
            enrollment.Id,
            WrongUserId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetByUserAndIdempotencyKeyHashAsync_ExistingKey_ReturnsEnrollment()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var idempotencyKeyHash = Guid.NewGuid().ToString("N");

        var enrollment = await CreateEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId,
            EnrollmentStatuses.Active,
            idempotencyKeyHash);

        var result = await _repository.GetByUserAndIdempotencyKeyHashAsync(
            scenario.UserId,
            idempotencyKeyHash);

        Assert.IsNotNull(result);
        Assert.AreEqual(enrollment.Id, result.Id);
        Assert.AreEqual(scenario.UserId, result.UserId);
        Assert.AreEqual(idempotencyKeyHash, result.IdempotencyKeyHash);
    }

    [TestMethod]
    public async Task GetByUserAndIdempotencyKeyHashAsync_WrongUserOrHash_ReturnsNull()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var idempotencyKeyHash = Guid.NewGuid().ToString("N");

        await CreateEnrollmentAsync(
            scenario.UserId,
            scenario.ClassId,
            EnrollmentStatuses.Active,
            idempotencyKeyHash);

        var resultByWrongUser = await _repository.GetByUserAndIdempotencyKeyHashAsync(
            WrongUserId,
            idempotencyKeyHash);

        var resultByWrongHash = await _repository.GetByUserAndIdempotencyKeyHashAsync(
            scenario.UserId,
            Guid.NewGuid().ToString("N"));

        Assert.IsNull(resultByWrongUser);
        Assert.IsNull(resultByWrongHash);
    }

    private async Task<Enrollment> CreateEnrollmentAsync(
        long userId,
        long classId,
        string status = EnrollmentStatuses.Active,
        string? idempotencyKeyHash = null)
    {
        var now = DateTime.UtcNow;

        var enrollment = new Enrollment
        {
            UserId = userId,
            ClassId = classId,
            Seats = Seats,
            InitialSeats = Seats,
            Status = status,
            IdempotencyKeyHash = idempotencyKeyHash ?? Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            CancelledAt = status == EnrollmentStatuses.Cancelled ? now : null
        };

        _dbContext.Set<Enrollment>().Add(enrollment);
        await _dbContext.SaveChangesAsync();

        return enrollment;
    }
}
