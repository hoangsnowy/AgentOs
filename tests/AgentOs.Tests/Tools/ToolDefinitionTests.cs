// Epic E1 — Validation rules for the Tool metadata record.

using AgentOs.Domain.Tools;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class ToolDefinitionTests
{
    [Fact]
    public void Validate_AllFieldsValid_DoesNotThrow()
    {
        var def = new ToolDefinition("echo", "Echoes the input.", """{"type":"object"}""");
        Should.NotThrow(() => def.Validate());
    }

    [Theory]
    [InlineData("", "desc", "{}")]
    [InlineData("   ", "desc", "{}")]
    [InlineData("name", "", "{}")]
    [InlineData("name", "   ", "{}")]
    [InlineData("name", "desc", "")]
    [InlineData("name", "desc", "   ")]
    public void Validate_MissingRequired_Throws(string name, string description, string schema)
    {
        var def = new ToolDefinition(name, description, schema);
        Should.Throw<System.ArgumentException>(() => def.Validate());
    }
}
