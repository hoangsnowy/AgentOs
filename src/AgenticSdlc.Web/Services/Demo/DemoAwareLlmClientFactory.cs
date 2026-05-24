// AgenticSdlc.Web/Services/Demo/DemoAwareLlmClientFactory.cs
// Phase 7 — Factory that wraps the real LlmClientFactory. If the circuit is in Demo mode
// (DemoRunContext.UseDemo) it returns DemoLlmClient; otherwise it delegates to the original factory
// to use Claude / Azure OpenAI per each agent's configuration. Preserves the thesis's
// Platform-Agnostic principle (swap the source without changing agents).

using System;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;

namespace AgenticSdlc.Web.Services.Demo;

/// <summary>Demo-aware factory that overrides the default <see cref="ILlmClientFactory"/>.</summary>
public sealed class DemoAwareLlmClientFactory : ILlmClientFactory
{
    private readonly DemoRunContext _context;
    private readonly DemoLlmClient _demo;
    private readonly LlmClientFactory _inner;

    /// <summary>Initialize with the circuit context, the demo client, and the real factory to delegate to.</summary>
    public DemoAwareLlmClientFactory(DemoRunContext context, DemoLlmClient demo, LlmClientFactory inner)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _demo = demo ?? throw new ArgumentNullException(nameof(demo));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public ILlmClient CreateDefault()
        => _context.UseDemo ? _demo : _inner.CreateDefault();

    /// <inheritdoc />
    public ILlmClient Create(string providerName)
        => _context.UseDemo ? _demo : _inner.Create(providerName);
}
