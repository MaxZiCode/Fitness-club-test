using ServiceLayer.Idempotency;

namespace WebApi.Tests.Services.Idempotency;

[TestClass]
public sealed class IdempotencyKeyHasherTests
{
    private IdempotencyKeyHasher _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _sut = new IdempotencyKeyHasher();
    }

    [TestMethod]
    public void Hash_EmptyString_ReturnsExpectedSha256Hash()
    {
        var result = _sut.Hash(string.Empty);

        Assert.AreEqual(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            result);
    }

    [TestMethod]
    public void Hash_KnownAsciiInput_ReturnsExpectedSha256Hash()
    {
        var result = _sut.Hash("abc");

        Assert.AreEqual(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            result);
    }

    [TestMethod]
    public void Hash_UnicodeInput_UsesUtf8Encoding()
    {
        var result = _sut.Hash("Привет, мир");

        Assert.AreEqual(
            "2A2E76364DF5AB8F0441D9C88BF7688F7F565F0F6B92A877CC94263E123021E3",
            result);
    }

    [TestMethod]
    public void Hash_UnicodeInputWithEmoji_ReturnsExpectedSha256Hash()
    {
        var result = _sut.Hash("ключ-🔑");

        Assert.AreEqual(
            "817281ACD7012441F865E3AE0C0535F31FC058CBC14C894929B2086588EDE9F3",
            result);
    }

    [TestMethod]
    public void Hash_SameInput_ReturnsSameHash()
    {
        var idempotencyKey = "test-idempotency-key";

        var firstResult = _sut.Hash(idempotencyKey);
        var secondResult = _sut.Hash(idempotencyKey);

        Assert.AreEqual(firstResult, secondResult);
    }

    [TestMethod]
    public void Hash_DifferentInputs_ReturnDifferentHashes()
    {
        var firstResult = _sut.Hash("key-1");
        var secondResult = _sut.Hash("key-2");

        Assert.AreNotEqual(firstResult, secondResult);
    }

    [TestMethod]
    public void Hash_CaseSensitiveInput_ReturnsDifferentHashes()
    {
        var lowerCaseResult = _sut.Hash("key");
        var upperCaseResult = _sut.Hash("KEY");

        Assert.AreNotEqual(lowerCaseResult, upperCaseResult);
    }

    [TestMethod]
    public void Hash_WhitespaceIsSignificant_ReturnsDifferentHashes()
    {
        var resultWithoutWhitespace = _sut.Hash("key");
        var resultWithLeadingWhitespace = _sut.Hash(" key");
        var resultWithTrailingWhitespace = _sut.Hash("key ");

        Assert.AreNotEqual(resultWithoutWhitespace, resultWithLeadingWhitespace);
        Assert.AreNotEqual(resultWithoutWhitespace, resultWithTrailingWhitespace);
    }

    [TestMethod]
    public void Hash_LongInput_ReturnsHashWithExpectedLength()
    {
        var idempotencyKey = new string('a', 100_000);

        var result = _sut.Hash(idempotencyKey);

        Assert.AreEqual(64, result.Length);
        Assert.AreEqual(result.ToUpperInvariant(), result);
    }

    [TestMethod]
    public void Hash_NullInput_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _sut.Hash(null!));
    }
}
