// Epic E2 — Adapter that exposes an Domain.Tools.ITool as a Microsoft.Extensions.AI AIFunction so
// FunctionInvokingChatClient can route LLM-emitted tool_use blocks back into the registered ITool.
// The AIFunction surface (Name, Description, JsonSchema) comes straight from ToolDefinition; the
// InvokeCoreAsync override serializes the model-emitted arguments back into the ITool's string
// Input, runs InvokeAsync, and returns the textual Output for the next LLM turn.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using Microsoft.Extensions.AI;

namespace AgentOs.Modules.Llm;

internal sealed class AIToolFunction : AIFunction
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ITool _tool;
    private readonly string _tenantId;
    private readonly string? _runId;
    private readonly JsonElement _schema;
    private readonly IToolPolicy? _policy;
    private readonly IToolInvocationLog? _log;

    public AIToolFunction(ITool tool, string tenantId, string? runId)
        : this(tool, tenantId, runId, policy: null, log: null)
    {
    }

    public AIToolFunction(ITool tool, string tenantId, string? runId, IToolPolicy? policy, IToolInvocationLog? log)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tool = tool;
        _tenantId = string.IsNullOrWhiteSpace(tenantId) ? "anonymous" : tenantId;
        _runId = runId;
        _policy = policy;
        _log = log;

        var def = tool.Definition;
        def.Validate();
        // ToolDefinition stores schema as a string for transport; parse it once at construction.
        _schema = JsonDocument.Parse(def.JsonInputSchema).RootElement.Clone();
    }

    public override string Name => _tool.Definition.Name;
    public override string Description => _tool.Definition.Description;
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var inputDict = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
        foreach (var kvp in arguments)
        {
            inputDict[kvp.Key] = kvp.Value;
        }

        var inputJson = JsonSerializer.Serialize(inputDict, SerializerOptions);
        var request = new ToolInvocationRequest(
            ToolName: _tool.Definition.Name,
            CallId: Guid.NewGuid().ToString("N"),
            Input: inputJson,
            TenantId: _tenantId,
            RunId: _runId);

        var started = DateTimeOffset.UtcNow;

        // Epic E5 — policy gate. Denied calls are surfaced to the LLM as a tool_result error
        // and recorded in the evidence log so the audit trail covers refusals as well.
        if (_policy is not null)
        {
            var decision = await _policy.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                var deniedReason = decision.Reason ?? $"Tool '{_tool.Definition.Name}' denied by policy.";
                await TryAppendEvidence(request, deniedReason, isError: true, started).ConfigureAwait(false);
                return deniedReason;
            }
        }

        var result = await _tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        var output = result.IsError
            ? (string.IsNullOrEmpty(result.ErrorMessage)
                ? $"Tool '{_tool.Definition.Name}' returned an error."
                : result.ErrorMessage!)
            : result.Output;

        await TryAppendEvidence(request, output, result.IsError, started).ConfigureAwait(false);
        return output;
    }

    private async ValueTask TryAppendEvidence(ToolInvocationRequest request, string output, bool isError, DateTimeOffset started)
    {
        if (_log is null)
        {
            return;
        }
        try
        {
            await _log.AppendAsync(new ToolInvocationEvidence(
                CallId: request.CallId,
                ToolName: request.ToolName,
                TenantId: request.TenantId,
                RunId: request.RunId,
                Input: request.Input,
                Output: output,
                IsError: isError,
                StartedUtc: started,
                FinishedUtc: DateTimeOffset.UtcNow)).ConfigureAwait(false);
        }
        catch
        {
            // Evidence is best-effort — a log failure must never break the tool call.
        }
    }
}
