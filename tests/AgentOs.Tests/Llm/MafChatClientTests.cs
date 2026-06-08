// M1 — proves MafChatClient pools its AzureOpenAIClient by (endpoint, key) instead of building a new one
// per request (socket/handler churn). Exercises the internal GetOrCreateClient seam (InternalsVisibleTo).

using AgentOs.Modules.Llm;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class MafChatClientTests
{
    [Fact]
    public void GetOrCreateClient_SameEndpointAndKey_ReturnsCachedInstance()
    {
        var a = MafChatClient.GetOrCreateClient("https://unit-test.openai.azure.com", "maf-key-1");
        var b = MafChatClient.GetOrCreateClient("https://unit-test.openai.azure.com", "maf-key-1");

        a.ShouldBeSameAs(b);
    }

    [Fact]
    public void GetOrCreateClient_DifferentKey_ReturnsDistinctInstance()
    {
        var a = MafChatClient.GetOrCreateClient("https://unit-test.openai.azure.com", "maf-key-1");
        var b = MafChatClient.GetOrCreateClient("https://unit-test.openai.azure.com", "maf-key-2");

        a.ShouldNotBeSameAs(b);
    }
}
