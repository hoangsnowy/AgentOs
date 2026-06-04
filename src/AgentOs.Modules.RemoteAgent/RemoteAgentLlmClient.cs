// LLM provider — instead of calling an LLM API (and spending tokens), dispatches the request to a
// connected remote agent via IRemoteAgentBroker and wraps the reply as an LlmResponse with zero
// cost. Registered as keyed ILlmClient "RemoteAgent" so LlmClientFactory resolves it by name.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.SharedKernel.Identity;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.RemoteAgent;

/// <summary>ILlmClient that routes a request to the current member's paired remote runner. Zero token
/// usage / cost. The target is the current request's tenant + member (ITenantContext), so the work runs
/// on that member's own machine and nobody else's.</summary>
public sealed class RemoteAgentLlmClient : ILlmClient
{
    public static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(120);

    private readonly IRemoteAgentBroker _broker;
    private readonly ITenantContext _tenant;
    private readonly ILogger<RemoteAgentLlmClient> _logger;

    /// <inheritdoc />
    public string Provider => "RemoteAgent";

    public RemoteAgentLlmClient(IRemoteAgentBroker broker, ITenantContext tenant, ILogger<RemoteAgentLlmClient> logger)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var target = new RunnerTarget(_tenant.TenantId, _tenant.UserId ?? string.Empty);
        if (!_broker.HasRunnerFor(target))
        {
            throw new LlmException(
                "No remote dev runner connected for you. Register a runner (POST /runners), start the AgentOS "
                + "remote agent on your dev machine with that runner id + token, or pick a different provider.",
                Provider);
        }

        var id = Guid.NewGuid().ToString("N");
        var execRequest = new RemoteExecRequest(id, request.SystemPrompt, request.UserPrompt, request.Model);

        // A single full-prompt dispatch (M3) caps at 120s, but an agentic issue-work run on the local
        // CLI (clone → build → test → push) needs minutes — the caller raises it via LlmRequest.Timeout.
        var timeout = request.Timeout ?? DispatchTimeout;

        var stopwatch = Stopwatch.StartNew();
        RemoteExecResult result;
        try
        {
            result = await _broker.DispatchAsync(execRequest, target, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new LlmException($"Remote agent timed out after {timeout.TotalSeconds:0}s.", Provider, innerException: ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new LlmException(ex.Message, Provider, innerException: ex);
        }
        stopwatch.Stop();

        if (!result.Ok)
        {
            throw new LlmException(result.Error ?? "Remote agent reported a failure.", Provider);
        }

        _logger.LogInformation("[RemoteAgent] request {Id} handled by a remote agent ({Count} connected); 0 API tokens spent.",
            id, _broker.AgentCount);

        return new LlmResponse(
            Content: result.Content,
            InputTokens: 0,
            OutputTokens: 0,
            CostUsd: 0m,
            Latency: stopwatch.Elapsed,
            Model: request.Model,
            Provider: Provider);
    }
}
