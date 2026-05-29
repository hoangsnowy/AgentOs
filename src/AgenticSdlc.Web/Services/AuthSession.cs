// AgenticSdlc.Web/Services/AuthSession.cs
// Phase 8.3 — Per-circuit auth state. Holds the JWT bearer token and the username after the
// user has signed in via LoginOverlay. Registered as scoped so each Blazor circuit has its
// own session; rehydrated from localStorage on circuit start.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Auth;

namespace AgenticSdlc.Web.Services;

/// <summary>Per-circuit auth state. Set by <c>LoginOverlay</c>; read by <c>HttpPipelineClient</c>.</summary>
public sealed class AuthSession : IAuthTokenProvider
{
    /// <summary>Current JWT bearer token, or <c>null</c> when anonymous.</summary>
    public string? Token { get; private set; }

    /// <summary>Username of the signed-in operator, or <c>null</c> when anonymous.</summary>
    public string? Username { get; private set; }

    /// <summary>Token expiry (UTC) — used to clear the session when the JWT lapses.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>Raised when <see cref="Token"/> changes — circuit observers re-render.</summary>
    public event Action? Changed;

    /// <summary>True when a non-expired JWT is held.</summary>
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && (ExpiresAtUtc is null || ExpiresAtUtc > DateTime.UtcNow);

    /// <summary>Set the session after a successful login.</summary>
    public void SetToken(string token, string username, DateTime? expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Token = token;
        Username = username;
        ExpiresAtUtc = expiresAtUtc;
        Changed?.Invoke();
    }

    /// <summary>Drop the session (logout / lock screen).</summary>
    public void Clear()
    {
        Token = null;
        Username = null;
        ExpiresAtUtc = null;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) { return ValueTask.FromResult<string?>(null); }
        return ValueTask.FromResult<string?>(Token);
    }
}
