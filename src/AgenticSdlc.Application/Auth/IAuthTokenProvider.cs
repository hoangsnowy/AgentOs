// AgenticSdlc.Application/Auth/IAuthTokenProvider.cs
// Phase 8.3 — Hand the current bearer token to HttpPipelineClient (and any other API-facing
// transport). The Web impl is per-circuit AuthSession; the in-process API host registers a
// null provider so the same DI registration boots either side.

using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.Application.Auth;

/// <summary>Provider for the JWT bearer token to attach to API calls.</summary>
public interface IAuthTokenProvider
{
    /// <summary>Returns the current bearer token, or <c>null</c> if anonymous.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>No-op provider — always returns null. Default registration when no real auth is wired.</summary>
public sealed class NullAuthTokenProvider : IAuthTokenProvider
{
    /// <summary>Singleton.</summary>
    public static readonly NullAuthTokenProvider Instance = new();
    private NullAuthTokenProvider() { }
    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
}
