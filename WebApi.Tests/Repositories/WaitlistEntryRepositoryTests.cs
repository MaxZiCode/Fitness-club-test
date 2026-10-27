using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests.Repositories;

[TestClass]
public class WaitlistEntryRepositoryTests
{
    private const long MissingId = long.MaxValue;
    private const long WrongUserId = long.MaxValue - 1;
    private const int EligibleSeats = 2;
    private const int InsufficientVisits = 1;
    private const int UserVisitsBonus = 1;
    private const int MaxSeats = 2;

    private IServiceScope _scope = null!;
    private ClubDbContext _dbContext = null!;
    private WaitlistEntryRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _scope = ApiTestSupport.Factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        _repository = new WaitlistEntryRepository(_dbContext.Set<WaitlistEntry>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
    }

    [TestMethod]
    public async Task GetByUserAndIdempotencyKeyHashAsync_ExistingEntry_ReturnsEntry()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var keyHash = Guid.NewGuid().ToString("N");

        var entry = await CreateWaitlistEntryAsync(
            scenario.UserId,
            scenario.ClassId,
            idempotencyKeyHash: keyHash);

        var result = await _repository.GetByUserAndIdempotencyKeyHashAsync(
            scenario.UserId,
            keyHash);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.Id, result.Id);
        Assert.AreEqual(scenario.UserId, result.UserId);
        Assert.AreEqual(keyHash, result.IdempotencyKeyHash);
    }

    [TestMethod]
    public async Task GetByUserAndIdempotencyKeyHashAsync_WrongUserOrHash_ReturnsNull()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();
        var keyHash = Guid.NewGuid().ToString("N");

        await CreateWaitlistEntryAsync(
            scenario.UserId,
            scenario.ClassId,
            idempotencyKeyHash: keyHash);

        var resultByWrongUser =
            await _repository.GetByUserAndIdempotencyKeyHashAsync(
                WrongUserId,
                keyHash);

        var resultByWrongHash =
            await _repository.GetByUserAndIdempotencyKeyHashAsync(
                scenario.UserId,
                Guid.NewGuid().ToString("N"));

        Assert.IsNull(resultByWrongUser);
        Assert.IsNull(resultByWrongHash);
    }

    [TestMethod]
    public async Task GetByIdAndUserAsync_ExistingEntry_ReturnsEntry()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();

        var entry = await CreateWaitlistEntryAsync(
            scenario.UserId,
            scenario.ClassId);

        var result = await _repository.GetByIdAndUserAsync(
            entry.Id,
            scenario.UserId);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.Id, result.Id);
        Assert.AreEqual(scenario.UserId, result.UserId);
        Assert.AreEqual(scenario.ClassId, result.ClassId);
        Assert.AreEqual(EntityState.Unchanged, _dbContext.Entry(result).State);
    }

    [TestMethod]
    public async Task GetByIdAndUserAsync_WrongUserOrId_ReturnsNull()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();

        var entry = await CreateWaitlistEntryAsync(
            scenario.UserId,
            scenario.ClassId);

        var resultByWrongUser = await _repository.GetByIdAndUserAsync(
            entry.Id,
            WrongUserId);

        var resultByWrongId = await _repository.GetByIdAndUserAsync(
            MissingId,
            scenario.UserId);

        Assert.IsNull(resultByWrongUser);
        Assert.IsNull(resultByWrongId);
    }

    [TestMethod]
    public async Task GetWaitingCountAndUserWaitingStatusAsync_ReturnsWaitingCountAndUserStatus()
    {
        var firstScenario = await ApiTestSupport.CreateScenarioAsync();
        var secondScenario = await ApiTestSupport.CreateScenarioAsync();
        var thirdScenario = await ApiTestSupport.CreateScenarioAsync();

        await CreateWaitlistEntryAsync(
            firstScenario.UserId,
            firstScenario.ClassId,
            status: WaitlistEntryStatuses.Waiting);

        await CreateWaitlistEntryAsync(
            secondScenario.UserId,
            firstScenario.ClassId,
            status: WaitlistEntryStatuses.Waiting);

        await CreateWaitlistEntryAsync(
            thirdScenario.UserId,
            firstScenario.ClassId,
            status: WaitlistEntryStatuses.Cancelled);

        var stateForWaitingUser =
            await _repository.GetWaitingCountAndUserWaitingStatusAsync(
                firstScenario.ClassId,
                firstScenario.UserId);

        var stateForUserWithoutWaitingEntry =
            await _repository.GetWaitingCountAndUserWaitingStatusAsync(
                firstScenario.ClassId,
                thirdScenario.UserId);

        Assert.AreEqual(
            new WaitlistQueueState(2, true),
            stateForWaitingUser);

        Assert.AreEqual(
            new WaitlistQueueState(2, false),
            stateForUserWithoutWaitingEntry);
    }

    [TestMethod]
    public async Task GetWaitingCountAndUserWaitingStatusAsync_NoWaitingEntries_ReturnsEmptyState()
    {
        var scenario = await ApiTestSupport.CreateScenarioAsync();

        var state = await _repository.GetWaitingCountAndUserWaitingStatusAsync(
            scenario.ClassId,
            scenario.UserId);

        Assert.AreEqual(new WaitlistQueueState(0, false), state);
    }

    [TestMethod]
    public async Task GetWaitingEntriesForPromotionAsync_FiltersEntriesAndOrdersResults()
    {
        var currentUser = await ApiTestSupport.CreateScenarioAsync();
        var firstEligibleUser = await ApiTestSupport.CreateScenarioAsync();
        var secondEligibleUser = await ApiTestSupport.CreateScenarioAsync();
        var insufficientVisitsUser = await ApiTestSupport.CreateScenarioAsync();
        var expiredPassUser = await ApiTestSupport.CreateScenarioAsync();
        var tooManySeatsUser = await ApiTestSupport.CreateScenarioAsync();
        var cancelledEntryUser = await ApiTestSupport.CreateScenarioAsync();

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var createdAt = new DateTime(
            2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        await SetPassAsync(currentUser.UserId, EligibleSeats - UserVisitsBonus, date.AddDays(10));
        await SetPassAsync(firstEligibleUser.UserId, EligibleSeats, date.AddDays(10));
        await SetPassAsync(secondEligibleUser.UserId, EligibleSeats, date.AddDays(10));
        await SetPassAsync(insufficientVisitsUser.UserId, InsufficientVisits, date.AddDays(10));
        await SetPassAsync(expiredPassUser.UserId, EligibleSeats, date.AddDays(-1));
        await SetPassAsync(tooManySeatsUser.UserId, EligibleSeats + 1, date.AddDays(10));
        await SetPassAsync(cancelledEntryUser.UserId, EligibleSeats, date.AddDays(10));

        var firstEligibleEntry = await CreateWaitlistEntryAsync(
            firstEligibleUser.UserId,
            currentUser.ClassId,
            createdAt: createdAt);

        var secondEligibleEntry = await CreateWaitlistEntryAsync(
            secondEligibleUser.UserId,
            currentUser.ClassId,
            createdAt: createdAt);

        var currentUserEntry = await CreateWaitlistEntryAsync(
            currentUser.UserId,
            currentUser.ClassId,
            seats: EligibleSeats,
            createdAt: createdAt.AddMinutes(-1));

        await CreateWaitlistEntryAsync(
            insufficientVisitsUser.UserId,
            currentUser.ClassId,
            seats: EligibleSeats,
            createdAt: createdAt.AddMinutes(-3));

        await CreateWaitlistEntryAsync(
            expiredPassUser.UserId,
            currentUser.ClassId,
            seats: EligibleSeats,
            createdAt: createdAt.AddMinutes(-4));

        await CreateWaitlistEntryAsync(
            tooManySeatsUser.UserId,
            currentUser.ClassId,
            seats: EligibleSeats + 1,
            createdAt: createdAt.AddMinutes(-5));

        await CreateWaitlistEntryAsync(
            cancelledEntryUser.UserId,
            currentUser.ClassId,
            status: WaitlistEntryStatuses.Cancelled,
            seats: EligibleSeats,
            createdAt: createdAt.AddMinutes(-6));

        var result = await _repository.GetWaitingEntriesForPromotionAsync(
            currentUser.ClassId,
            MaxSeats,
            currentUser.UserId,
            UserVisitsBonus,
            date);

        CollectionAssert.AreEqual(
            new[]
            {
                currentUserEntry.Id,
                firstEligibleEntry.Id,
                secondEligibleEntry.Id
            },
            result.Select(entry => entry.Id).ToArray());
    }

    private async Task SetPassAsync(
        long userId,
        int remainingVisits,
        DateOnly validUntil)
    {
        var pass = await _dbContext.Set<Pass>()
            .SingleAsync(pass => pass.UserId == userId);

        pass.RemainingVisits = remainingVisits;
        pass.ValidUntil = validUntil;

        await _dbContext.SaveChangesAsync();
    }

    private async Task<WaitlistEntry> CreateWaitlistEntryAsync(
        long userId,
        long classId,
        string status = WaitlistEntryStatuses.Waiting,
        int seats = EligibleSeats,
        DateTime? createdAt = null,
        string? idempotencyKeyHash = null,
        int position = 1)
    {
        var entry = new WaitlistEntry
        {
            UserId = userId,
            ClassId = classId,
            Status = status,
            Seats = seats,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IdempotencyKeyHash = idempotencyKeyHash ?? Guid.NewGuid().ToString("N"),
            InitialPosition = position
        };

        _dbContext.Set<WaitlistEntry>().Add(entry);
        await _dbContext.SaveChangesAsync();

        return entry;
    }
}
