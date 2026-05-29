// LLM provider — instead of calling an LLM API (and spending tokens), dispatches the request to a
// connected remote agent via IRemoteAgentBroker and wraps the reply as an LlmResponse with zero
// cost. Registered as keyed ILlmClient "RemoteAgent" so LlmClientFactory resolves it by name.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Modules.RemoteAgent;

/// <summary>ILlmClient that routes a request to a connected remote agent. Zero token usage / cost.</summary>
public sealed class RemoteAgentLlmClient : ILlmClient
{
    public static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(120);

    private readonly IRemoteAgentBroker _broker;
    private readonly ILogger<RemoteAgentLlmClient> _logger;

    /// <inheritdoc />
    public string Provider => "RemoteAgent";

    public RemoteAgentLlmClient(IRemoteAgentBroker broker, ILogger<RemoteAgentLlmClient> logger)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (!_broker.HasAgent)
        {
            throw new LlmException(
                "No remote dev agent connected. Start the AgentSdlc remote agent on the dev machine and pair it, "
                + "or pick a different provider.",
                Provider);
        }

        var id = Guid.NewGuid().ToString("N");
        var execRequest = new RemoteExecRequest(id, request.SystemPrompt, request.UserPrompt, request.Model);

        var stopwatch = Stopwatch.StartNew();
        RemoteExecResult result;
        try
        {
            result = await _broker.DispatchAsync(execRequest, DispatchTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new LlmException($"Remote agent timed out after {DispatchTimeout.TotalSeconds:0}s.", Provider, innerException: ex);
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
