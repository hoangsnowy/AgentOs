// Epic E3 — McpToolAdapter behaviour: JSON input parsing, invoker delegation, error surfacing.
// The adapter doesn't need a live MCP server — McpToolInvoker is a delegate so we stub it inline.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Mcp;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Mcp;

public sealed class McpToolAdapterTests
{
    private static ToolDefinition Def() => new(
        "github.search_issues",
        "Search GitHub issues.",
        """{"type":"object","properties":{"query":{"type":"string"}}}""");

    [Fact]
    public void Adapter_ExposesDefinition()
    {
        var adapter = new McpToolAdapter(Def(), (_, _) => Task.FromResult("ok"));

        adapter.Definition.Name.ShouldBe("github.search_issues");
        adapter.Definition.JsonInputSchema.ShouldContain("query");
    }

    [Fact]
    public async Task Invoke_ValidJson_DelegatesToInvokerAndReturnsSuccess()
    {
        IReadOnlyDictionary<string, object?>? captured = null;
        McpToolInvoker invoker = (args, _) =>
        {
            captured = args;
            return Task.FromResult("issues: 3");
        };
        var adapter = new McpToolAdapter(Def(), invoker);

        var req = new ToolInvocationRequest(
            "github.search_issues", "call-1",
            """{"query":"bug","limit":10}""",
            "tenant-1");
        var result = await adapter.InvokeAsync(req);

        result.IsError.ShouldBeFalse();
        result.Output.ShouldBe("issues: 3");
        captured.ShouldNotBeNull();
        captured!["query"]?.ToString().ShouldBe("bug");
    }

    [Fact]
    public async Task Invoke_EmptyObjectInput_PassesEmptyDictionary()
    {
        IReadOnlyDictionary<string, object?>? captured = null;
        McpToolInvoker invoker = (args, _) =>
        {
            captured = args;
            return Task.FromResult("ok");
        };
        var adapter = new McpToolAdapter(Def(), invoker);

        await adapter.InvokeAsync(new ToolInvocationRequest("github.search_issues", "call-2", "{}", "tenant-1"));

        captured.ShouldNotBeNull();
        captured!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Invoke_InvalidJson_ReturnsParseError()
    {
        var adapter = new McpToolAdapter(Def(), (_, _) => Task.FromResult("ok"));

        var result = await adapter.InvokeAsync(new ToolInvocationRequest(
            "github.search_issues", "call-3", "not-json", "tenant-1"));

        result.IsError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain("invalid JSON");
    }

    [Fact]
    public async Task Invoke_InvokerThrows_ReturnsErrorWithMessage()
    {
        McpToolInvoker invoker = (_, _) => throw new InvalidOperationException("server unreachable");
        var adapter = new McpToolAdapter(Def(), invoker);

        var result = await adapter.InvokeAsync(new ToolInvocationRequest(
            "github.search_issues", "call-4", """{"query":"x"}""", "tenant-1"));

        result.IsError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain("server unreachable");
    }

    [Fact]
    public async Task Invoke_CancellationPropagates()
    {
        McpToolInvoker invoker = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("ok");
        };
        var adapter = new McpToolAdapter(Def(), invoker);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            adapter.InvokeAsync(
                new ToolInvocationRequest("github.search_issues", "call-5", """{"query":"x"}""", "tenant-1"),
                cts.Token));
    }

    [Fact]
    public void Ctor_NullDefinition_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new McpToolAdapter(null!, (_, _) => Task.FromResult("ok")));
    }

    [Fact]
    public void Ctor_NullInvoker_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new McpToolAdapter(Def(), null!));
    }
}
