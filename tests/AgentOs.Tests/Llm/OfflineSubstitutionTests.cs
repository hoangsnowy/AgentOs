// Settings → Providers → Test connection rendered a green "OK · Offline" for a probe whose configured
// provider never answered: LlmClientFactory appends the keyless Offline client to every failover chain
// when Llm:OfflineFallback is on, the call "succeeds" with canned text, and the UI set _testOk = true
// unconditionally. A connectivity probe that greenlights an unreachable provider asserts the opposite of
// what happened — the failure it hides is less damaging than the false assurance. Both hosts now gate on
// OfflineLlmClient.IsSubstituteFor.

using AgentOs.Modules.Llm;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public class OfflineSubstitutionTests
{
    [Theory]
    [InlineData("Claude")]
    [InlineData("AzureOpenAI")]
    [InlineData("MAF")]
    [InlineData("RemoteAgent")]
    public void IsSubstituteFor_OfflineAnsweredForRealProvider_IsTrue(string requested)
    {
        OfflineLlmClient.IsSubstituteFor(requested, OfflineLlmClient.ProviderName).ShouldBeTrue();
    }

    [Fact]
    public void IsSubstituteFor_OfflineRequestedAndAnswered_IsFalse()
    {
        // Asking for Offline and getting Offline is exactly what was ordered, not a silent stand-in.
        OfflineLlmClient.IsSubstituteFor(OfflineLlmClient.ProviderName, OfflineLlmClient.ProviderName)
            .ShouldBeFalse();
    }

    [Fact]
    public void IsSubstituteFor_RealProviderAnswered_IsFalse()
    {
        OfflineLlmClient.IsSubstituteFor("Claude", "Claude").ShouldBeFalse();
        // Failover between two REAL providers is a genuine success — only the canned client is a lie.
        OfflineLlmClient.IsSubstituteFor("Claude", "AzureOpenAI").ShouldBeFalse();
    }

    [Fact]
    public void IsSubstituteFor_IsCaseInsensitive()
    {
        OfflineLlmClient.IsSubstituteFor("claude", "offline").ShouldBeTrue();
        OfflineLlmClient.IsSubstituteFor("OFFLINE", "Offline").ShouldBeFalse();
    }

    [Fact]
    public void IsSubstituteFor_NullAnswer_IsFalse()
    {
        OfflineLlmClient.IsSubstituteFor("Claude", null).ShouldBeFalse();
    }
}
