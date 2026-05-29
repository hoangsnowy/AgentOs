// Cross-cutting JWT bearer-token provider so transports (HttpPipelineClient and any other API-facing
// HTTP client) can attach the current request's auth token. The Web impl is a per-circuit AuthSession;
// hosts without auth wire NullAuthTokenProvider so the same DI registration boots either side.

using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.SharedKernel.Identity;

/// <summary>Provider for the JWT bearer token to attach to API calls.</summary>
public interface IAuthTokenProvider
{
    /// <summary>Returns the current bearer token, or <c>null</c> if anonymous.</summary>
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>No-op provider — always returns null. Default registration when no real auth is wired.</summary>
public sealed class NullAuthTokenProvider : IAuthTokenProvider
{
    public static readonly NullAuthTokenProvider Instance = new();
    private NullAuthTokenProvider() { }
    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
}
