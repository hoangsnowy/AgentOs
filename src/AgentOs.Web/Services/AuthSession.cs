// Per-circuit auth state. Reads the signed-in identity from the OIDC cookie via IHttpContextAccessor;
// HttpPipelineClient consumes the access token (cached at circuit init) when forwarding to a remote API.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace AgentOs.Web.Services;

/// <summary>Per-circuit auth state surfaced to UI components and to <c>HttpPipelineClient</c>.</summary>
public sealed class AuthSession : IAuthTokenProvider
{
    private readonly IHttpContextAccessor _accessor;
    private string? _cachedToken;
    private bool _tokenLoaded;

    public AuthSession(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <summary>Username of the signed-in user, or <c>null</c> when anonymous.</summary>
    public string? Username => _accessor.HttpContext?.User?.Identity?.Name;

    /// <summary>True when the current request principal is authenticated by the cookie scheme.</summary>
    public bool IsAuthenticated => _accessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    /// <summary>Bearer access token captured from the OIDC cookie. <c>null</c> when anonymous or not yet loaded.</summary>
    public string? Token => _cachedToken;

    /// <inheritdoc />
    public async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenLoaded) { return _cachedToken; }
        var ctx = _accessor.HttpContext;
        if (ctx is null) { return null; }
        _cachedToken = await ctx.GetTokenAsync("access_token").ConfigureAwait(false);
        _tokenLoaded = true;
        return _cachedToken;
    }
}
