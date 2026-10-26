using Common.Caching;
using DataAccessLayer.Entities;
using Microsoft.Extensions.Options;
using Moq;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class EnrollmentCardCacheServiceTests
{
    private long _userId;
    private long _otherUserId;
    private long _userVersion;
    private long _enrollmentVersion;
    private long _nextUserVersion;
    private long _nextEnrollmentVersion;
    private TimeSpan _ttl;
    private DateTime _now;

    private Mock<ICacheService> _cacheService = null!;

    private EnrollmentCardCacheService _sut = null!;

    private Class _class = null!;
    private Enrollment _enrollment = null!;
    private Pass _pass = null!;
    private EnrollmentCard _card = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 1;
        _otherUserId = 2;
        _userVersion = 7;
        _enrollmentVersion = 11;
        _nextUserVersion = _userVersion + 1;
        _nextEnrollmentVersion = _enrollmentVersion + 1;
        _ttl = TimeSpan.FromMinutes(15);
        _now = new DateTime(
            2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        _cacheService = new Mock<ICacheService>();

        _class = new Class
        {
            Id = 2,
            Room = new Room()
        };

        _enrollment = new Enrollment
        {
            Id = 3,
            UserId = _userId,
            ClassId = _class.Id,
            Seats = 2,
            InitialSeats = 2,
            Status = EnrollmentStatuses.Active,
            CreatedAt = _now,
            Class = _class
        };

        _pass = new Pass
        {
            UserId = _userId,
            RemainingVisits = 10
        };

        _card = EnrollmentCard.FromEnrollment(_enrollment, _pass);

        _cacheService
            .Setup(x => x.GetManyAsync<long>(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                _userVersion,
                _enrollmentVersion
            ]);

        _cacheService
            .Setup(x => x.GetAsync<EnrollmentCard>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnrollmentCard?)null);

        _cacheService
            .Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<EnrollmentCard>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new EnrollmentCardCacheService(
            _cacheService.Object,
            Options.Create(new EnrollmentCardCacheOptions
            {
                EnrollmentCardTtl = _ttl
            }));
    }

    [TestMethod]
    public async Task GetOrCreateAsync_CacheHit_ReturnsCachedCardWithoutLoading()
    {
        var cacheKey =
            $"enrollment-card:{_userId}:{_userVersion}:{_enrollment.Id}:{_enrollmentVersion}";

        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        var loaderCalled = false;

        _cacheService
            .Setup(x => x.GetAsync<EnrollmentCard>(cacheKey, ct))
            .ReturnsAsync(_card);

        var result = await _sut.GetOrCreateAsync(
            _userId,
            _enrollment.Id,
            _ =>
            {
                loaderCalled = true;
                return Task.FromResult<EnrollmentCard?>(_card);
            },
            ct);

        Assert.AreSame(_card, result);
        Assert.IsFalse(loaderCalled);

        _cacheService.Verify(
            x => x.GetManyAsync<long>(
                It.Is<IReadOnlyList<string>>(keys =>
                    keys.SequenceEqual(new[]
                    {
                        $"enrollment-card-user-version:{_userId}",
                        $"enrollment-card-enrollment-version:{_userId}:{_enrollment.Id}"
                    })),
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.GetAsync<EnrollmentCard>(cacheKey, ct),
            Times.Once);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<EnrollmentCard>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_CacheMiss_LoadsAndCachesCard()
    {
        var cacheKey =
            $"enrollment-card:{_userId}:{_userVersion}:{_enrollment.Id}:{_enrollmentVersion}";

        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        CancellationToken? loaderToken = null;

        var result = await _sut.GetOrCreateAsync(
            _userId,
            _enrollment.Id,
            token =>
            {
                loaderToken = token;
                return Task.FromResult<EnrollmentCard?>(_card);
            },
            ct);

        Assert.AreSame(_card, result);
        Assert.AreEqual(ct, loaderToken);

        _cacheService.Verify(
            x => x.GetAsync<EnrollmentCard>(cacheKey, ct),
            Times.Once);

        _cacheService.Verify(
            x => x.SetAsync(
                cacheKey,
                _card,
                _ttl,
                ct),
            Times.Once);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_LoaderReturnsNull_DoesNotCache()
    {
        var loaderCallCount = 0;

        var result = await _sut.GetOrCreateAsync(
            _userId,
            _enrollment.Id,
            _ =>
            {
                loaderCallCount++;
                return Task.FromResult<EnrollmentCard?>(null);
            });

        Assert.IsNull(result);
        Assert.AreEqual(1, loaderCallCount);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<EnrollmentCard>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_UsesDifferentKeyWhenVersionsChange()
    {
        var initialCacheKey =
            $"enrollment-card:{_userId}:{_userVersion}:{_enrollment.Id}:{_enrollmentVersion}";

        var nextCacheKey =
            $"enrollment-card:{_userId}:{_nextUserVersion}:{_enrollment.Id}:{_nextEnrollmentVersion}";

        _cacheService
            .SetupSequence(x => x.GetManyAsync<long>(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                _userVersion,
                _enrollmentVersion
            ])
            .ReturnsAsync(
            [
                _nextUserVersion,
                _nextEnrollmentVersion
            ]);

        var firstResult = await _sut.GetOrCreateAsync(
            _userId,
            _enrollment.Id,
            _ => Task.FromResult<EnrollmentCard?>(_card));

        var secondResult = await _sut.GetOrCreateAsync(
            _userId,
            _enrollment.Id,
            _ => Task.FromResult<EnrollmentCard?>(_card));

        Assert.AreSame(_card, firstResult);
        Assert.AreSame(_card, secondResult);

        _cacheService.Verify(
            x => x.GetAsync<EnrollmentCard>(
                initialCacheKey,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.GetAsync<EnrollmentCard>(
                nextCacheKey,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.SetAsync(
                initialCacheKey,
                _card,
                _ttl,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.SetAsync(
                nextCacheKey,
                _card,
                _ttl,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_VersionLookupFails_PropagatesException()
    {
        var expectedException = new InvalidOperationException();

        _cacheService
            .Setup(x => x.GetManyAsync<long>(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var loaderCalled = false;

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.GetOrCreateAsync(
                    _userId,
                    _enrollment.Id,
                    _ =>
                    {
                        loaderCalled = true;
                        return Task.FromResult<EnrollmentCard?>(_card);
                    }));

        Assert.AreSame(expectedException, actualException);
        Assert.IsFalse(loaderCalled);

        _cacheService.Verify(
            x => x.GetAsync<EnrollmentCard>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_CacheReadFails_PropagatesExceptionWithoutLoading()
    {
        var expectedException = new InvalidOperationException();

        _cacheService
            .Setup(x => x.GetAsync<EnrollmentCard>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var loaderCalled = false;

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.GetOrCreateAsync(
                    _userId,
                    _enrollment.Id,
                    _ =>
                    {
                        loaderCalled = true;
                        return Task.FromResult<EnrollmentCard?>(_card);
                    }));

        Assert.AreSame(expectedException, actualException);
        Assert.IsFalse(loaderCalled);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<EnrollmentCard>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_LoaderFails_PropagatesExceptionWithoutCaching()
    {
        var expectedException = new InvalidOperationException();

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.GetOrCreateAsync(
                    _userId,
                    _enrollment.Id,
                    _ => Task.FromException<EnrollmentCard?>(
                        expectedException)));

        Assert.AreSame(expectedException, actualException);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<EnrollmentCard>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_CacheWriteFails_PropagatesException()
    {
        var expectedException = new InvalidOperationException();

        _cacheService
            .Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<EnrollmentCard>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var actualException =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _sut.GetOrCreateAsync(
                    _userId,
                    _enrollment.Id,
                    _ => Task.FromResult<EnrollmentCard?>(_card)));

        Assert.AreSame(expectedException, actualException);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                _card,
                _ttl,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_ForwardsCancellationTokenToLoader()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        CancellationToken? loaderToken = null;

        var result = await _sut.GetOrCreateAsync(
            _userId,
            _enrollment.Id,
            token =>
            {
                loaderToken = token;
                return Task.FromResult<EnrollmentCard?>(_card);
            },
            ct);

        Assert.AreSame(_card, result);
        Assert.AreEqual(ct, loaderToken);

        _cacheService.Verify(
            x => x.GetManyAsync<long>(
                It.IsAny<IReadOnlyList<string>>(),
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.GetAsync<EnrollmentCard>(
                It.IsAny<string>(),
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                _card,
                _ttl,
                ct),
            Times.Once);
    }

    [TestMethod]
    public async Task InvalidateEnrollmentsForUserAsync_IncrementsCorrectUserVersion()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        await _sut.InvalidateEnrollmentsForUserAsync(_userId, ct);

        _cacheService.Verify(
            x => x.IncrementAsync(
                $"enrollment-card-user-version:{_userId}",
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.IncrementManyAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InvalidateEnrollmentsForUsersAsync_EmptyCollection_DoesNotCallCache()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        var userIds = Array.Empty<long>();

        await _sut.InvalidateEnrollmentsForUsersAsync(userIds, ct);

        _cacheService.Verify(
            x => x.IncrementAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.IncrementManyAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InvalidateEnrollmentsForUsersAsync_DuplicateSingleUser_UsesSingleIncrement()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        var userIds = new[] { _userId, _userId, _userId };

        await _sut.InvalidateEnrollmentsForUsersAsync(userIds, ct);

        _cacheService.Verify(
            x => x.IncrementAsync(
                $"enrollment-card-user-version:{_userId}",
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.IncrementManyAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InvalidateEnrollmentsForUsersAsync_MultipleUsersWithDuplicates_IncrementsUniqueUserVersions()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var userIds = new[]
        {
            _userId,
            _otherUserId,
            _userId,
            _otherUserId,
            _userId
        };

        var expectedKeys = new[]
        {
            $"enrollment-card-user-version:{_userId}",
            $"enrollment-card-user-version:{_otherUserId}"
        };

        await _sut.InvalidateEnrollmentsForUsersAsync(userIds, ct);

        _cacheService.Verify(
            x => x.IncrementManyAsync(
                It.Is<IReadOnlyList<string>>(keys =>
                    keys.SequenceEqual(expectedKeys)),
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.IncrementAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InvalidateEnrollmentForUserAsync_IncrementsCorrectEnrollmentVersion()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        await _sut.InvalidateEnrollmentForUserAsync(
            _userId,
            _enrollment.Id,
            ct);

        _cacheService.Verify(
            x => x.IncrementAsync(
                $"enrollment-card-enrollment-version:{_userId}:{_enrollment.Id}",
                ct),
            Times.Once);

        _cacheService.Verify(
            x => x.IncrementManyAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
