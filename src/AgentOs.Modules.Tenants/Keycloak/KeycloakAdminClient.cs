// Typed HttpClient over the Keycloak Admin REST API. Caches the master-realm admin token for the
// majority of its lifetime so back-to-back provisioning calls don't re-login on every hop.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Tenants.Keycloak;

/// <summary>Typed HttpClient over the Keycloak Admin REST API. Base address is the realm root.</summary>
public sealed class KeycloakAdminClient : IKeycloakAdminClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly KeycloakAdminOptions _options;
    private readonly ILogger<KeycloakAdminClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAt;

    public KeycloakAdminClient(HttpClient http, IOptions<KeycloakAdminOptions> options, ILogger<KeycloakAdminClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
        _logger = logger;
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateUserAsync(
        string username,
        string email,
        string tenantId,
        IReadOnlyList<string> realmRoles,
        bool sendVerifyEmail,
        string? password = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(realmRoles);

        var token = await GetAdminTokenAsync(ct).ConfigureAwait(false);

        // Keycloak's default user-profile config requires firstName + lastName for a "fully set up"
        // account — without them the token endpoint rejects login with "Account is not fully set up".
        // We don't ask for them on the sign-up form so fall back to the username for both.
        // emailVerified:false whenever we're about to send a verify-email action (realm-level
        // verifyEmail then blocks login until the link is clicked). When no email goes out — admin
        // invite without sendVerifyEmail, or self-signup with no email — leave it true so the user
        // can log in immediately.
        var emailVerified = !sendVerifyEmail;
        object createBody = string.IsNullOrEmpty(password)
            ? new
            {
                username,
                email,
                firstName = username,
                lastName = username,
                enabled = true,
                emailVerified,
                attributes = new Dictionary<string, string[]> { ["tenant"] = new[] { tenantId } },
            }
            : new
            {
                username,
                email,
                firstName = username,
                lastName = username,
                enabled = true,
                emailVerified,
                attributes = new Dictionary<string, string[]> { ["tenant"] = new[] { tenantId } },
                credentials = new[]
                {
                    new { type = "password", value = password, temporary = false },
                },
            };
        using var create = BuildRequest(HttpMethod.Post, $"admin/realms/{_options.Realm}/users", token, createBody);
        using var createResp = await _http.SendAsync(create, ct).ConfigureAwait(false);
        if (!createResp.IsSuccessStatusCode)
        {
            var body = await createResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Keycloak create-user failed ({(int)createResp.StatusCode}): {body}");
        }

        var location = createResp.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("Keycloak create-user response missing Location header.");
        var userId = location.TrimEnd('/').Split('/').Last();

        if (realmRoles.Count > 0)
        {
            var roleMappings = new List<object>(realmRoles.Count);
            foreach (var roleName in realmRoles)
            {
                using var roleReq = BuildRequest(HttpMethod.Get, $"admin/realms/{_options.Realm}/roles/{Uri.EscapeDataString(roleName)}", token);
                using var roleResp = await _http.SendAsync(roleReq, ct).ConfigureAwait(false);
                if (!roleResp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Keycloak role {Role} lookup failed: {Status}", roleName, roleResp.StatusCode);
                    continue;
                }
                var roleJson = await roleResp.Content.ReadFromJsonAsync<KeycloakRoleDto>(ct).ConfigureAwait(false);
                if (roleJson is null)
                {
                    continue;
                }
                roleMappings.Add(new { id = roleJson.Id, name = roleJson.Name });
            }

            if (roleMappings.Count > 0)
            {
                using var grant = BuildRequest(HttpMethod.Post,
                    $"admin/realms/{_options.Realm}/users/{userId}/role-mappings/realm", token, roleMappings);
                using var grantResp = await _http.SendAsync(grant, ct).ConfigureAwait(false);
                if (!grantResp.IsSuccessStatusCode)
                {
                    var body = await grantResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException($"Keycloak grant-roles failed ({(int)grantResp.StatusCode}): {body}");
                }
            }
        }

        if (sendVerifyEmail)
        {
            // Self-signup users (password supplied) already chose a password → skip UPDATE_PASSWORD;
            // admin-invited users (no password) need both: pick a password and verify the email.
            var actionList = string.IsNullOrEmpty(password) ? InviteActions : VerifyOnlyActions;
            using var actions = BuildRequest(HttpMethod.Put,
                $"admin/realms/{_options.Realm}/users/{userId}/execute-actions-email", token,
                actionList);
            using var actionsResp = await _http.SendAsync(actions, ct).ConfigureAwait(false);
            if (!actionsResp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Keycloak execute-actions-email failed for {UserId}: {Status} — user created but no invite email sent",
                    userId, actionsResp.StatusCode);
            }
        }

        return userId;
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var token = await GetAdminTokenAsync(ct).ConfigureAwait(false);
        using var req = BuildRequest(HttpMethod.Delete, $"admin/realms/{_options.Realm}/users/{userId}", token);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Keycloak delete-user failed ({(int)resp.StatusCode}): {body}");
        }
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresAt)
        {
            return _cachedToken;
        }
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresAt)
            {
                return _cachedToken;
            }
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _options.ClientId,
                ["username"] = _options.Username,
                ["password"] = _options.Password,
            });
            using var resp = await _http.PostAsync("realms/master/protocol/openid-connect/token", form, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Keycloak admin token request failed ({(int)resp.StatusCode}): {body}");
            }
            var dto = await resp.Content.ReadFromJsonAsync<TokenDto>(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Keycloak admin token response is empty.");
            _cachedToken = dto.AccessToken;
            _cachedTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, dto.ExpiresIn - 30));
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: SerializerOptions);
        }
        return req;
    }

    public void Dispose() => _tokenLock.Dispose();

    private static readonly string[] InviteActions = new[] { "UPDATE_PASSWORD", "VERIFY_EMAIL" };
    private static readonly string[] VerifyOnlyActions = new[] { "VERIFY_EMAIL" };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record KeycloakRoleDto(string Id, string Name);
}
