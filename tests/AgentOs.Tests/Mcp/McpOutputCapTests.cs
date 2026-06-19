// MCP tool output flows into the next LLM turn and is persisted as evidence, so a hostile/buggy server
// must not be able to return unbounded text. CapOutput truncates with a visible marker.

using AgentOs.Modules.Mcp;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Mcp;

public sealed class McpOutputCapTests
{
    [Fact]
    public void CapOutput_UnderLimit_ReturnedVerbatim()
    {
        const string text = "small result";
        McpClientHost.CapOutput(text).ShouldBe(text);
    }

    [Fact]
    public void CapOutput_AtLimit_NotTruncated()
    {
        var text = new string('x', McpClientHost.MaxOutputChars);
        McpClientHost.CapOutput(text).ShouldBe(text);
    }

    [Fact]
    public void CapOutput_OverLimit_TruncatedWithMarker()
    {
        var text = new string('x', McpClientHost.MaxOutputChars + 5_000);
        var capped = McpClientHost.CapOutput(text);

        capped.Length.ShouldBeLessThan(text.Length);
        capped.ShouldStartWith(new string('x', 100));
        capped.ShouldEndWith("(truncated)");
        capped.Length.ShouldBe(McpClientHost.MaxOutputChars + "\n…(truncated)".Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void CapOutput_NullOrEmpty_ReturnedAsIs(string? text)
    {
        McpClientHost.CapOutput(text!).ShouldBe(text);
    }
}
