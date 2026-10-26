using ServiceLayer.Shared;

namespace WebApi.Tests.Services.Shared;

[TestClass]
public sealed class DateTimeServiceTests
{
    private const long TicksPerMicrosecond = 10;
    private const long MaxTruncatedTicks = TicksPerMicrosecond - 1;

    private DateTimeService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _sut = new DateTimeService();
    }

    [TestMethod]
    public void GetNow_ReturnsUtcDateTime()
    {
        var result = _sut.GetNow();

        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
    }

    [TestMethod]
    public void GetNow_ReturnsTimeAlignedToMicrosecondPrecision()
    {
        var result = _sut.GetNow();

        Assert.AreEqual(0L, result.Ticks % TicksPerMicrosecond);
    }

    [TestMethod]
    public void GetNow_ReturnsTimeWithinCurrentTimeWindow()
    {
        var before = DateTime.UtcNow;

        var result = _sut.GetNow();

        var after = DateTime.UtcNow;

        Assert.IsTrue(result >= before.AddTicks(-MaxTruncatedTicks));
        Assert.IsTrue(result <= after);
    }
}
