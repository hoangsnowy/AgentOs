// "Remote dev-IDE agent" runtime — server-side dispatch seam. The server hands a codegen request to
// a connected remote agent (running in/near the dev's IDE) and awaits the result, instead of paying
// for an LLM API call. Transport-agnostic broker: a transport (e.g. SignalR hub) registers connected
// agents, pushes RequestDispatched, and calls Complete with the agent's reply.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.Modules.RemoteAgent;

/// <summary>A codegen task sent to a remote agent.</summary>
public sealed record RemoteExecRequest(string Id, string SystemPrompt, string UserPrompt, string Model);

/// <summary>A remote agent's reply for a <see cref="RemoteExecRequest"/>.</summary>
public sealed record RemoteExecResult(string Id, bool Ok, string Content, string? Error);

/// <summary>Dispatch seam between the server and connected remote agents.</summary>
public interface IRemoteAgentBroker
{
    bool HasAgent { get; }

    int AgentCount { get; }

    event Action<RemoteExecRequest>? RequestDispatched;

    Task<RemoteExecResult> DispatchAsync(RemoteExecRequest request, TimeSpan timeout, CancellationToken cancellationToken = default);

    IDisposable RegisterAgent(string agentId);

    void Complete(RemoteExecResult result);
}

/// <summary>In-process broker. Singleton; thread-safe.</summary>
public sealed class InProcessRemoteAgentBroker : IRemoteAgentBroker
{
    private readonly ConcurrentDictionary<string, byte> _agents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RemoteExecResult>> _pending = new(StringComparer.Ordinal);

    public bool HasAgent => !_agents.IsEmpty;

    public int AgentCount => _agents.Count;

    public event Action<RemoteExecRequest>? RequestDispatched;

    public async Task<RemoteExecResult> DispatchAsync(RemoteExecRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!HasAgent)
        {
            throw new InvalidOperationException("No remote agent connected.");
        }

        var tcs = new TaskCompletionSource<RemoteExecResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var registration = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(request.Id, out var pending))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    pending.TrySetCanceled(cancellationToken);
                }
                else
                {
                    pending.TrySetException(new TimeoutException($"Remote agent did not respond within {timeout.TotalSeconds:0}s."));
                }
            }
        });

        try
        {
            RequestDispatched?.Invoke(request);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    public IDisposable RegisterAgent(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        _agents[agentId] = 1;
        return new Registration(this, agentId);
    }

    public void Complete(RemoteExecResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_pending.TryRemove(result.Id, out var tcs))
        {
            tcs.TrySetResult(result);
        }
    }

    private void Unregister(string agentId) => _agents.TryRemove(agentId, out _);

    private sealed class Registration : IDisposable
    {
        private readonly InProcessRemoteAgentBroker _broker;
        private readonly string _agentId;
        private bool _disposed;

        public Registration(InProcessRemoteAgentBroker broker, string agentId)
        {
            _broker = broker;
            _agentId = agentId;
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _broker.Unregister(_agentId);
        }
    }
}
