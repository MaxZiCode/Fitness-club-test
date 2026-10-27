using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests.Repositories;

[TestClass]
public class PassRepositoryTests
{
    private const long MissingUserId = long.MaxValue;

    private IServiceScope _scope = null!;
    private ClubDbContext _dbContext = null!;
    private PassRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _scope = ApiTestSupport.Factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        _repository = new PassRepository(_dbContext.Set<Pass>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
    }

    [TestMethod]
    public async Task GetByUserNoTrackingAsync_ExistingPass_ReturnsPassWithoutTracking()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();

        var pass = await _repository.GetByUserNoTrackingAsync(scenario.UserId);

        Assert.AreEqual(scenario.UserId, pass.UserId);
        Assert.AreEqual(scenario.InitialPassVisits, pass.RemainingVisits);
        Assert.AreEqual(scenario.PassValidUntil, pass.ValidUntil);
        Assert.AreEqual(EntityState.Detached, _dbContext.Entry(pass).State);
    }

    [TestMethod]
    public async Task GetByUserNoTrackingAsync_MissingPass_ThrowsInvalidOperationException()
    {
        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _repository.GetByUserNoTrackingAsync(MissingUserId));

        Assert.AreEqual(
            $"No pass found for user {MissingUserId}.",
            exception.Message);
    }

    [TestMethod]
    public async Task GetLockedByUserIdAsync_ExistingPass_ReturnsTrackedPass()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();

        var pass = await _repository.GetLockedByUserIdAsync(scenario.UserId);

        Assert.AreEqual(scenario.UserId, pass.UserId);
        Assert.AreEqual(scenario.InitialPassVisits, pass.RemainingVisits);
        Assert.AreEqual(scenario.PassValidUntil, pass.ValidUntil);
        Assert.AreEqual(EntityState.Unchanged, _dbContext.Entry(pass).State);
    }

    [TestMethod]
    public async Task GetLockedByUserIdAsync_MissingPass_ThrowsInvalidOperationException()
    {
        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _repository.GetLockedByUserIdAsync(MissingUserId));

        Assert.AreEqual(
            $"No pass found for user {MissingUserId}.",
            exception.Message);
    }

    [TestMethod]
    public async Task GetLockedByUserIdsAsync_EmptyCollection_ReturnsEmptyList()
    {
        var passes = await _repository.GetLockedByUserIdsAsync(
            Array.Empty<long>());

        Assert.IsNotNull(passes);
        Assert.AreEqual(0, passes.Count);
    }

    [TestMethod]
    public async Task GetLockedByUserIdsAsync_ReturnsOnlyRequestedPassesOrderedById()
    {
        var firstScenario = await ApiTestSupport.CreateScenarioAsync();
        var excludedScenario = await ApiTestSupport.CreateScenarioAsync();
        var lastScenario = await ApiTestSupport.CreateScenarioAsync();

        var userIds = new[]
        {
            lastScenario.UserId,
            MissingUserId,
            firstScenario.UserId
        };

        var expectedUserIds = await _dbContext.Set<Pass>()
            .AsNoTracking()
            .Where(pass => userIds.Contains(pass.UserId))
            .OrderBy(pass => pass.Id)
            .Select(pass => pass.UserId)
            .ToListAsync();

        var passes = await _repository.GetLockedByUserIdsAsync(userIds);

        CollectionAssert.AreEqual(
            expectedUserIds,
            passes.Select(pass => pass.UserId).ToList());

        Assert.IsFalse(
            passes.Any(pass => pass.UserId == excludedScenario.UserId));
    }
}
