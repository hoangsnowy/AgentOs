// Epic E3 — Bridges a connected MCP server tool back into AgentOs's Domain.Tools.ITool surface so
// it shows up in the IToolRegistry alongside locally-implemented tools (BuildVerifierTool, ...).
// The MCP SDK's McpClientTool is itself an AIFunction; we don't reuse that here — agents always go
// through ITool / IToolRegistry, and the LLM-side adapter (AIToolFunction) wraps every ITool the
// same way regardless of whether the impl is local or remote MCP.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;

namespace AgentOs.Modules.Mcp;

/// <summary>Per-call invoker signature so tests can stub MCP without mocking the SDK's McpClientTool.</summary>
public delegate Task<string> McpToolInvoker(
    IReadOnlyDictionary<string, object?> arguments,
    CancellationToken cancellationToken);

/// <summary>ITool wrapper that calls a remote MCP tool via the supplied invoker delegate.</summary>
internal sealed class McpToolAdapter : ITool
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly McpToolInvoker _invoker;

    public McpToolAdapter(ToolDefinition definition, McpToolInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(invoker);
        definition.Validate();
        Definition = definition;
        _invoker = invoker;
    }

    /// <inheritdoc />
    public ToolDefinition Definition { get; }

    /// <inheritdoc />
    public async Task<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        Dictionary<string, object?> args;
        try
        {
            args = ParseArguments(request.Input);
        }
        catch (JsonException ex)
        {
            return ToolInvocationResult.Error(
                request.CallId,
                $"MCP tool '{Definition.Name}' got invalid JSON input: {ex.Message}");
        }

        try
        {
            var output = await _invoker(args, cancellationToken).ConfigureAwait(false);
            return ToolInvocationResult.Success(request.CallId, output ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ModelContextProtocol.McpException ex) { return OnInvokeFailed(ex); }
        catch (System.Net.Http.HttpRequestException ex) { return OnInvokeFailed(ex); }
        catch (System.Text.Json.JsonException ex) { return OnInvokeFailed(ex); }
        catch (System.IO.IOException ex) { return OnInvokeFailed(ex); }
        catch (TimeoutException ex) { return OnInvokeFailed(ex); }
        catch (InvalidOperationException ex) { return OnInvokeFailed(ex); }

        ToolInvocationResult OnInvokeFailed(Exception ex) =>
            ToolInvocationResult.Error(
                request.CallId,
                $"MCP tool '{Definition.Name}' invocation failed: {ex.Message}");
    }

    private static Dictionary<string, object?> ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = json };
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            dict[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText(), SerializerOptions);
        }
        return dict;
    }
}
