// Epic E1 — Registry behaviour: register/resolve/list/unregister, duplicate detection, validation.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Registry;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class InMemoryToolRegistryTests
{
    [Fact]
    public void Register_ValidTool_AddsToRegistry()
    {
        var registry = new InMemoryToolRegistry();
        var tool = FakeTool("echo");

        registry.Register(tool);

        registry.Resolve("echo").ShouldBe(tool);
        registry.List().Count.ShouldBe(1);
        registry.List()[0].Name.ShouldBe("echo");
    }

    [Fact]
    public void Register_DuplicateName_Throws()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(FakeTool("echo"));

        var ex = Should.Throw<ToolException>(() => registry.Register(FakeTool("echo")));
        ex.Message.ShouldContain("echo");
    }

    [Fact]
    public void Register_InvalidDefinition_Throws()
    {
        var registry = new InMemoryToolRegistry();
        var bad = Substitute.For<ITool>();
        bad.Definition.Returns(new ToolDefinition(string.Empty, "x", "{}"));

        Should.Throw<System.ArgumentException>(() => registry.Register(bad));
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsNull()
    {
        var registry = new InMemoryToolRegistry();
        registry.Resolve("missing").ShouldBeNull();
    }

    [Fact]
    public void Resolve_NullOrEmpty_ReturnsNull()
    {
        var registry = new InMemoryToolRegistry();
        registry.Resolve(string.Empty).ShouldBeNull();
        registry.Resolve("   ").ShouldBeNull();
    }

    [Fact]
    public void Unregister_KnownName_RemovesAndReturnsTrue()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(FakeTool("echo"));

        registry.Unregister("echo").ShouldBeTrue();
        registry.Resolve("echo").ShouldBeNull();
        registry.List().ShouldBeEmpty();
    }

    [Fact]
    public void Unregister_UnknownName_ReturnsFalse()
    {
        var registry = new InMemoryToolRegistry();
        registry.Unregister("missing").ShouldBeFalse();
    }

    [Fact]
    public void List_MultipleTools_ReturnsAllDefinitions()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(FakeTool("a"));
        registry.Register(FakeTool("b"));
        registry.Register(FakeTool("c"));

        var names = registry.List().Select(d => d.Name).OrderBy(n => n).ToArray();
        names.ShouldBe(["a", "b", "c"]);
    }

    private static ITool FakeTool(string name)
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition(
            name,
            $"Fake tool {name}",
            """{"type":"object","properties":{}}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToolInvocationResult.Success("call-1", "ok")));
        return tool;
    }
}
