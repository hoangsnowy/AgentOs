// An IChatClient decorator that also owns an underlying disposable resource. Anthropic.SDK exposes
// its chat surface as AnthropicClient.Messages — taking that endpoint drops the AnthropicClient handle,
// leaking its HttpClient for the process lifetime (CodeQL cs/local-not-disposed at the factory site).
// Wrapping keeps the owner alive and disposes it together with the inner client when the pool shuts down.

using System;
using System.Threading;
using Microsoft.Extensions.AI;

namespace AgentOs.Modules.Llm;

internal sealed class OwningChatClient : DelegatingChatClient
{
    private readonly IDisposable _owned;
    private int _disposed;

    public OwningChatClient(IChatClient inner, IDisposable owned)
        : base(inner)
        => _owned = owned ?? throw new ArgumentNullException(nameof(owned));

    protected override void Dispose(bool disposing)
    {
        // Idempotent: the pool may dispose a base client both via its FunctionInvoking wrapper's
        // cascade and directly, and AnthropicClient.Dispose is not guaranteed double-dispose-safe.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (disposing)
        {
            _owned.Dispose();
        }

        base.Dispose(disposing);
    }
}
