// Defense-in-depth host allowlist for tenant-supplied workspace hosts (GitHub Enterprise / Azure
// DevOps Server base URLs, the "find boards/repos" token probes). The connect-time SsrfGuard already
// refuses sockets to private/loopback/link-local IPs; this layer narrows the *public* surface too, so
// a tenant cannot steer a server-side call at an arbitrary external host — only the provider public
// hosts plus any self-hosted hosts an operator explicitly allows via Workspaces:AllowedHosts.
//
// A null/blank host means "the provider's public default" (github.com / dev.azure.com) and is always
// allowed — the baseline defaults cover it. Configured entries AUGMENT the defaults (they never drop
// github.com/dev.azure.com), so enabling a GHE host can't silently break public-cloud connect flows.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Workspaces.Security;

/// <summary>Binds <c>Workspaces:AllowedHosts</c> — extra hosts (GitHub Enterprise Server, Azure DevOps
/// Server) an operator trusts on top of the always-allowed provider public hosts.</summary>
internal sealed class WorkspaceHostOptions
{
    /// <summary>Operator-supplied hosts to allow in addition to the public defaults. Each entry may be a
    /// bare host (<c>github.mycorp.com</c>) or a URL (<c>https://github.mycorp.com</c>); only the host
    /// component is used. Subdomains of an entry are allowed.</summary>
    public IList<string> AllowedHosts { get; } = [];
}

/// <summary>Decides whether a tenant-supplied workspace host may be contacted.</summary>
internal interface IWorkspaceHostPolicy
{
    /// <summary><c>true</c> if <paramref name="host"/> is null/blank (the provider's public default) or
    /// resolves to an allowed host (exact match or a subdomain of one).</summary>
    bool IsAllowed(string? host);
}

internal sealed class WorkspaceHostPolicy : IWorkspaceHostPolicy
{
    // Provider public hosts that are always trusted. api.github.com is covered by the subdomain rule;
    // *.visualstudio.com (legacy ADO) likewise.
    private static readonly string[] DefaultHosts = ["github.com", "dev.azure.com", "visualstudio.com"];

    private readonly string[] _allowed;

    public WorkspaceHostPolicy(IOptions<WorkspaceHostOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _allowed = DefaultHosts
            .Concat(options.Value.AllowedHosts ?? [])
            .Select(NormalizeHost)
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsAllowed(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true; // null/blank => provider public default, covered by the baseline allowlist.
        }
        if (!TryExtractHost(host, out var h))
        {
            return false; // unparseable => reject rather than guess.
        }
        foreach (var allowed in _allowed)
        {
            if (h.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || h.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeHost(string entry) =>
        TryExtractHost(entry, out var h) ? h : string.Empty;

    // Accepts a full URL (https://host/path), a scheme-less host, or host:port/path; returns the bare
    // host component. Rejects empty/whitespace.
    private static bool TryExtractHost(string value, out string host)
    {
        host = string.Empty;
        var s = value.Trim();
        if (s.Length == 0)
        {
            return false;
        }
        if (Uri.TryCreate(s, UriKind.Absolute, out var abs) && !string.IsNullOrEmpty(abs.Host))
        {
            host = abs.Host;
            return true;
        }
        // Bare host[:port][/path] — strip any path then any port.
        var slash = s.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            s = s[..slash];
        }
        var colon = s.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            s = s[..colon];
        }
        if (s.Length == 0)
        {
            return false;
        }
        host = s;
        return true;
    }
}
