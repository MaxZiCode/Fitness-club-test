using System.Text;
using System.Text.Json;
using ServiceLayer.Schedule;

namespace WebApi.Tests.Services.Schedule;

[TestClass]
public sealed class ScheduleContinuationTokenServiceTests
{
    private ScheduleContinuationTokenService _sut = null!;

    private DateTime _startsAt;
    private long _classId;
    private ScheduleCursor _cursor = null!;

    [TestInitialize]
    public void Setup()
    {
        _startsAt = new DateTime(
            2026, 10, 26, 12, 34, 56, 789, DateTimeKind.Utc)
            .AddTicks(1234);

        _classId = 42;

        _cursor = new ScheduleCursor(_startsAt, _classId);

        _sut = new ScheduleContinuationTokenService();
    }

    [TestMethod]
    public void Encode_ValidCursor_ReturnsBase64EncodedJson()
    {
        var token = _sut.Encode(_cursor);

        Assert.IsFalse(string.IsNullOrWhiteSpace(token));

        var json = Encoding.UTF8.GetString(
            Convert.FromBase64String(token));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(
            _startsAt,
            root.GetProperty(nameof(ScheduleCursor.StartsAt)).GetDateTime());

        Assert.AreEqual(
            _classId,
            root.GetProperty(nameof(ScheduleCursor.ClassId)).GetInt64());
    }

    [TestMethod]
    public void EncodeDecode_ValidCursor_PreservesAllValues()
    {
        var token = _sut.Encode(_cursor);

        var result = _sut.Decode(token);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(_cursor, result.Value);
        Assert.AreEqual(_startsAt, result.Value.StartsAt);
        Assert.AreEqual(_classId, result.Value.ClassId);
    }

    [TestMethod]
    public void Encode_SameCursor_ReturnsSameToken()
    {
        var firstToken = _sut.Encode(_cursor);
        var secondToken = _sut.Encode(_cursor);

        Assert.AreEqual(firstToken, secondToken);
    }

    [TestMethod]
    public void Decode_ValidJson_ReturnsCursor()
    {
        var json = """
            {"StartsAt":"2026-10-26T12:34:56Z","ClassId":42}
            """;

        var token = ToBase64(json);

        var result = _sut.Decode(token);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(
            new DateTime(2026, 10, 26, 12, 34, 56, DateTimeKind.Utc),
            result.Value.StartsAt);
        Assert.AreEqual(_classId, result.Value.ClassId);
    }

    [TestMethod]
    public void EncodeDecode_MinimumDateAndZeroClassId_PreservesValues()
    {
        var cursor = new ScheduleCursor(DateTime.MinValue, 0);

        var token = _sut.Encode(cursor);
        var result = _sut.Decode(token);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(cursor, result.Value);
    }

    [TestMethod]
    public void EncodeDecode_MaximumDateAndClassId_PreservesValues()
    {
        var cursor = new ScheduleCursor(
            DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
            long.MaxValue);

        var token = _sut.Encode(cursor);
        var result = _sut.Decode(token);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(cursor, result.Value);
    }

    [TestMethod]
    public void Decode_InvalidBase64_ReturnsInvalidContinuationToken()
    {
        var result = _sut.Decode("not-base64!!!");

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_EmptyToken_ReturnsInvalidContinuationToken()
    {
        var result = _sut.Decode(string.Empty);

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_Base64EncodedMalformedJson_ReturnsInvalidContinuationToken()
    {
        var token = ToBase64("""{"StartsAt":""");

        var result = _sut.Decode(token);

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_Base64EncodedNullJson_ReturnsInvalidContinuationToken()
    {
        var token = ToBase64("null");

        var result = _sut.Decode(token);

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_Base64EncodedArrayJson_ReturnsInvalidContinuationToken()
    {
        var token = ToBase64("[]");

        var result = _sut.Decode(token);

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_InvalidDateValue_ReturnsInvalidContinuationToken()
    {
        var json = """
            {"StartsAt":"not-a-date","ClassId":42}
            """;

        var result = _sut.Decode(ToBase64(json));

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_NonNumericClassId_ReturnsInvalidContinuationToken()
    {
        var json = """
            {"StartsAt":"2026-10-26T12:34:56Z","ClassId":"42"}
            """;

        var result = _sut.Decode(ToBase64(json));

        AssertInvalidToken(result);
    }

    [TestMethod]
    public void Decode_NullToken_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _sut.Decode(null!));
    }

    private static string ToBase64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static void AssertInvalidToken(
        ErrorOr.ErrorOr<ScheduleCursor> result)
    {
        Assert.IsTrue(result.IsError);
        Assert.AreEqual(
            ScheduleErrors.InvalidContinuationToken.Code,
            result.FirstError.Code);
    }
}
