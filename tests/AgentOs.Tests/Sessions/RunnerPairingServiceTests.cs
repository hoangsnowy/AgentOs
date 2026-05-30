// M3 — unit tests for the runner pairing primitive: issue a high-entropy token + salted hash, verify
// only the right token against the stored hash, and reject everything else without throwing.

using AgentOs.Domain.Sessions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public sealed class RunnerPairingServiceTests
{
    private readonly RunnerPairingService _sut = new();

    [Fact]
    public void Issue_ProducesTokenAndSaltedHash_ThatRoundTrips()
    {
        var secret = _sut.Issue();

        secret.Token.ShouldNotBeNullOrWhiteSpace();
        secret.TokenHash.ShouldStartWith("sha256$");
        secret.TokenHash.ShouldNotContain(secret.Token); // plaintext never embedded in the hash
        _sut.Verify(secret.Token, secret.TokenHash).ShouldBeTrue();
    }

    [Fact]
    public void Verify_WrongToken_ReturnsFalse()
    {
        var secret = _sut.Issue();
        _sut.Verify("not-the-token", secret.TokenHash).ShouldBeFalse();
    }

    [Fact]
    public void Issue_IsSaltedSoTwoIssuesOfDifferentTokensDiffer()
    {
        var a = _sut.Issue();
        var b = _sut.Issue();

        a.Token.ShouldNotBe(b.Token);
        a.TokenHash.ShouldNotBe(b.TokenHash);
        // Cross-verification must fail: each token only matches its own hash.
        _sut.Verify(a.Token, b.TokenHash).ShouldBeFalse();
        _sut.Verify(b.Token, a.TokenHash).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("sha256$nothex$nothex")]
    [InlineData("md5$00$00")]
    public void Verify_MalformedStoredHash_ReturnsFalseNeverThrows(string storedHash)
    {
        Should.NotThrow(() => _sut.Verify("any-token", storedHash).ShouldBeFalse());
    }

    [Fact]
    public void Verify_EmptyPresentedToken_ReturnsFalse()
    {
        var secret = _sut.Issue();
        _sut.Verify("", secret.TokenHash).ShouldBeFalse();
    }
}
