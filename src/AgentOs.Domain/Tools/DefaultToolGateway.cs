// M1 — Default IToolGateway. A pure composition of the Domain tool interfaces (no framework
// dependencies), so it lives in Domain and is reusable by both Modules.Llm (AIToolFunction) and
// the remote-session executor without a cross-module reference. Policy + log are optional: a null
// policy skips gating, a null log skips evidence — but a tool call is ALWAYS recorded when a log
// is present, including policy denials and tool errors. Evidence writes are best-effort and never
// break the call.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Domain.Tools;

/// <summary>Default <see cref="IToolGateway"/>: policy gate -> invoke -> evidence.</summary>
public sealed class DefaultToolGateway : IToolGateway
{
    private readonly IToolPolicy? _policy;
    private readonly IToolInvocationLog? _log;

    public DefaultToolGateway(IToolPolicy? policy = null, IToolInvocationLog? log = null)
    {
        _policy = policy;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<ToolGatewayResult> InvokeAsync(
        ITool tool,
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(request);

        var started = DateTimeOffset.UtcNow;

        // Policy gate. Denied calls are surfaced to the model as a tool_result error and recorded
        // in the evidence log so the audit trail covers refusals as well as successes.
        if (_policy is not null)
        {
            var decision = await _policy.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                var reason = decision.Reason ?? $"Tool '{request.ToolName}' denied by policy.";
                await TryAppendAsync(request, reason, isError: true, started).ConfigureAwait(false);
                return new ToolGatewayResult(reason, IsError: true, Denied: true);
            }
        }

        var result = await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        var output = result.IsError
            ? (string.IsNullOrEmpty(result.ErrorMessage)
                ? $"Tool '{request.ToolName}' returned an error."
                : result.ErrorMessage!)
            : result.Output;

        await TryAppendAsync(request, output, result.IsError, started).ConfigureAwait(false);
        return new ToolGatewayResult(output, result.IsError);
    }

    private async Task TryAppendAsync(ToolInvocationRequest request, string output, bool isError, DateTimeOffset started)
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
                FinishedUtc: DateTimeOffset.UtcNow,
                SessionId: request.SessionId)).ConfigureAwait(false);
        }
        // Evidence is best-effort — a log failure must never break the tool call. Narrow to the
        // failure shapes a log sink realistically raises (the EF sink swallows its own DB errors).
        catch (ObjectDisposedException ex) { _ = ex.Message; }
        catch (IOException ex) { _ = ex.Message; }
        catch (TimeoutException ex) { _ = ex.Message; }
        catch (InvalidOperationException ex) { _ = ex.Message; }
    }
}
