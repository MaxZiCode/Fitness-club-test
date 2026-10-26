using DataAccessLayer.Entities;
using DataAccessLayer.UnitOfWork;
using Moq;
using ServiceLayer.Enrollments;

namespace WebApi.Tests.Services.Enrollments;

[TestClass]
public sealed class EnrollmentCardServiceTests
{
    private long _userId;

    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IEnrollmentCardCacheService> _cacheService = null!;

    private EnrollmentCardService _sut = null!;

    private Class _class = null!;
    private Enrollment _enrollment = null!;
    private Pass _pass = null!;

    [TestInitialize]
    public void Setup()
    {
        _userId = 1;

        _unitOfWork = new Mock<IUnitOfWork>
        {
            DefaultValue = DefaultValue.Mock
        };

        _cacheService = new Mock<IEnrollmentCardCacheService>();

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
            CreatedAt = DateTime.UtcNow,
            Class = _class
        };

        _pass = new Pass
        {
            UserId = _userId,
            RemainingVisits = 10
        };

        _cacheService
            .Setup(x => x.GetOrCreateAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Func<CancellationToken, Task<EnrollmentCard?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                long userId,
                long enrollmentId,
                Func<CancellationToken, Task<EnrollmentCard?>> factory,
                CancellationToken ct) => factory(ct));

        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_enrollment);

        Mock.Get(_unitOfWork.Object.Pass)
            .Setup(x => x.GetByUserNoTrackingAsync(
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_pass);

        _sut = new EnrollmentCardService(
            _unitOfWork.Object,
            _cacheService.Object);
    }

    [TestMethod]
    public async Task GetEnrollmentCardAsync_CacheHit_ReturnsCachedCardWithoutDatabaseCalls()
    {
        var cachedCard = EnrollmentCard.FromEnrollment(
            _enrollment,
            _pass);

        _cacheService
            .Setup(x => x.GetOrCreateAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<Func<CancellationToken, Task<EnrollmentCard?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedCard);

        var command = new GetEnrollmentCardCommand(
            _enrollment.Id,
            _userId);

        var result = await _sut.GetEnrollmentCardAsync(command);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(cachedCard, result.Value);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetByUserNoTrackingAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        _cacheService.Verify(
            x => x.GetOrCreateAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<Func<CancellationToken, Task<EnrollmentCard?>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetEnrollmentCardAsync_CacheMissAndEnrollmentExists_ReturnsCardFromDatabase()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var command = new GetEnrollmentCardCommand(
            _enrollment.Id,
            _userId);

        var result = await _sut.GetEnrollmentCardAsync(
            command,
            cancellationToken);

        Assert.IsFalse(result.IsError);
        Assert.IsNotNull(result.Value);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
                _enrollment.Id,
                _userId,
                cancellationToken),
            Times.Once);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetByUserNoTrackingAsync(
                _userId,
                cancellationToken),
            Times.Once);

        _cacheService.Verify(
            x => x.GetOrCreateAsync(
                _userId,
                _enrollment.Id,
                It.IsAny<Func<CancellationToken, Task<EnrollmentCard?>>>(),
                cancellationToken),
            Times.Once);
    }

    [TestMethod]
    public async Task GetEnrollmentCardAsync_EnrollmentNotFound_ReturnsNotFound()
    {
        Mock.Get(_unitOfWork.Object.Enrollment)
            .Setup(x => x.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enrollment?)null);

        var command = new GetEnrollmentCardCommand(
            _enrollment.Id,
            _userId);

        var result = await _sut.GetEnrollmentCardAsync(command);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ErrorOr.Error.NotFound().Code,
            result.FirstError.Code);

        Mock.Get(_unitOfWork.Object.Pass).Verify(
            x => x.GetByUserNoTrackingAsync(
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Never);

        Mock.Get(_unitOfWork.Object.Enrollment).Verify(
            x => x.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
                _enrollment.Id,
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
