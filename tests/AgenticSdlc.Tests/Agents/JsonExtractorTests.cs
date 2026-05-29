// AgenticSdlc.Tests/Agents/JsonExtractorTests.cs
// Phase 4 — JsonExtractor handles 3 forms: direct, fenced, prose-wrapped.

using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Modules.Pipeline.Agents;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Agents;

public class JsonExtractorTests
{
    private sealed class Probe
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    [Fact]
    public void Deserialize_DirectJson_Works()
    {
        var raw = """{"name":"foo","value":42}""";
        var result = JsonExtractor.Deserialize<Probe>(raw, "Test");
        result.Name.ShouldBe("foo");
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Deserialize_MarkdownFence_StrippedAndParsed()
    {
        var raw = """
            Here is the JSON:
            ```json
            {"name":"bar","value":99}
            ```
            """;
        var result = JsonExtractor.Deserialize<Probe>(raw, "Test");
        result.Name.ShouldBe("bar");
        result.Value.ShouldBe(99);
    }

    [Fact]
    public void Deserialize_PlainFenceNoLang_StrippedAndParsed()
    {
        var raw = """
            ```
            {"name":"baz","value":7}
            ```
            """;
        var result = JsonExtractor.Deserialize<Probe>(raw, "Test");
        result.Name.ShouldBe("baz");
    }

    [Fact]
    public void Deserialize_ProseWrapped_ExtractsBraced()
    {
        var raw = "Sure! Here's the result: {\"name\":\"qux\",\"value\":1} — let me know if you need more.";
        var result = JsonExtractor.Deserialize<Probe>(raw, "Test");
        result.Name.ShouldBe("qux");
    }

    [Fact]
    public void Deserialize_CamelCaseInsensitive_Works()
    {
        var raw = """{"Name":"camel","Value":3}""";
        var result = JsonExtractor.Deserialize<Probe>(raw, "Test");
        result.Name.ShouldBe("camel");
    }

    [Fact]
    public void Deserialize_TrailingComma_AllowedByOptions()
    {
        var raw = """{"name":"trailing","value":5,}""";
        var result = JsonExtractor.Deserialize<Probe>(raw, "Test");
        result.Name.ShouldBe("trailing");
    }

    [Fact]
    public void Deserialize_NotJson_ThrowsLlmException()
    {
        var raw = "I don't know how to do that.";
        var ex = Should.Throw<LlmException>(() => JsonExtractor.Deserialize<Probe>(raw, "Test"));
        ex.Provider.ShouldBe("Test");
        ex.Message.ShouldContain("Test:");
    }
}
