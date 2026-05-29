// Bridges the in-process IRemoteAgentBroker to the SignalR hub: when the gateway dispatches a
// request, run it past the approval gate, then push "Execute" to connected agents. Agent replies
// arrive via the hub's CompleteRequest, which resolves the broker's pending task.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Modules.RemoteAgent;

/// <summary>Hosted service that wires the broker's dispatch event to the SignalR hub.</summary>
public sealed class RemoteAgentTransport : IHostedService
{
    private readonly IRemoteAgentBroker _broker;
    private readonly IHubContext<RemoteAgentHub> _hub;
    private readonly IRemoteExecApprover _approver;
    private readonly ILogger<RemoteAgentTransport> _logger;

    public RemoteAgentTransport(
        IRemoteAgentBroker broker,
        IHubContext<RemoteAgentHub> hub,
        IRemoteExecApprover approver,
        ILogger<RemoteAgentTransport> logger)
    {
        _broker = broker;
        _hub = hub;
        _approver = approver;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _broker.RequestDispatched += OnDispatched;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _broker.RequestDispatched -= OnDispatched;
        return Task.CompletedTask;
    }

    private void OnDispatched(RemoteExecRequest request) => _ = PushAsync(request);

    private async Task PushAsync(RemoteExecRequest request)
    {
        try
        {
            if (!await _approver.ApproveAsync(request).ConfigureAwait(false))
            {
                _logger.LogWarning("[RemoteAgent] request {Id} denied by approval gate.", request.Id);
                _broker.Complete(new RemoteExecResult(request.Id, false, string.Empty, "Denied by approval gate."));
                return;
            }
            await _hub.Clients.All.SendAsync("Execute", request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _broker.Complete(new RemoteExecResult(request.Id, false, string.Empty, $"Transport error: {ex.Message}"));
        }
    }
}
