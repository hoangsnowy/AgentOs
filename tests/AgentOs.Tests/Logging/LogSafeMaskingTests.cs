// Batch 3 — credential masking: secret-shaped substrings must not survive into log sinks.
using AgentOs.SharedKernel.Logging;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Logging;

public class LogSafeMaskingTests
{
    [Fact]
    public void MaskSecrets_AnthropicKey_MasksPayload()
    {
        var masked = LogSafe.MaskSecrets("calling with key sk-ant-api03-AbCdEfGh123456 done");
        masked.ShouldNotContain("AbCdEfGh123456");
        masked.ShouldContain("sk-ant-");
        masked.ShouldContain("***");
    }

    [Theory]
    [InlineData("ghp_1234567890abcdef")]
    [InlineData("gho_1234567890abcdef")]
    [InlineData("ghs_1234567890abcdef")]
    [InlineData("github_pat_1234567890abcdef")]
    [InlineData("GHP_1234567890ABCDEF")] // uppercased copies must not bypass the mask
    [InlineData("SK-ANT-API03-ABCDEF12345")]
    public void MaskSecrets_TokenPrefixes_AnyCase_MasksPayload(string token)
    {
        var masked = LogSafe.MaskSecrets($"token={token}");
        masked.ShouldNotContain(token);
        masked.ShouldContain("***");
    }

    [Fact]
    public void MaskSecrets_BearerHeader_MasksToken()
    {
        var masked = LogSafe.MaskSecrets("Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.payload.sig");
        masked.ShouldNotContain("eyJhbGciOiJSUzI1NiJ9.payload.sig");
        masked.ShouldContain("Bearer ");
    }

    [Fact]
    public void MaskSecrets_ConnectionStringPassword_Masked()
    {
        var masked = LogSafe.MaskSecrets("Host=db;Username=app;Password=Hunter2!;Database=agentos");
        masked.ShouldNotContain("Hunter2!");
        masked.ShouldContain("Password=***");
        masked.ShouldContain("Host=db");
    }

    [Fact]
    public void MaskSecrets_PlainText_Unchanged() =>
        LogSafe.MaskSecrets("pipeline run 42 finished in 3.2s").ShouldBe("pipeline run 42 finished in 3.2s");

    [Fact]
    public void Scrub_NewlinesAndSecrets_BothNeutralised()
    {
        var scrubbed = LogSafe.Scrub("line1\r\ninjected ghp_abcdef123456789");
        scrubbed.ShouldNotContain("\n");
        scrubbed.ShouldNotContain("ghp_abcdef123456789");
    }

    [Fact]
    public void Scrub_Null_ReturnsPlaceholder() => LogSafe.Scrub(null).ShouldBe("(null)");
}
