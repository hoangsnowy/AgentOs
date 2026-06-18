// Defense-in-depth host allowlist for tenant-supplied workspace hosts. Null/blank => provider public
// default (allowed); public hosts + their subdomains allowed; off-list hosts rejected; operator
// Workspaces:AllowedHosts entries augment the defaults (never drop github.com/dev.azure.com).

using AgentOs.Modules.Workspaces.Security;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Workspaces;

public sealed class WorkspaceHostPolicyTests
{
    private static IWorkspaceHostPolicy Policy(params string[] allowed)
    {
        var opts = new WorkspaceHostOptions();
        foreach (var a in allowed)
        {
            opts.AllowedHosts.Add(a);
        }
        return new WorkspaceHostPolicy(Options.Create(opts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_NullOrBlank_IsAllowed_ProviderPublicDefault(string? host)
    {
        Policy().IsAllowed(host).ShouldBeTrue();
    }

    [Theory]
    [InlineData("github.com")]
    [InlineData("api.github.com")]                 // subdomain of github.com
    [InlineData("https://github.com")]
    [InlineData("https://api.github.com/graphql")] // URL form, path stripped
    [InlineData("dev.azure.com")]
    [InlineData("myorg.visualstudio.com")]          // legacy ADO subdomain
    [InlineData("GitHub.com")]                       // case-insensitive
    public void IsAllowed_PublicHostsAndSubdomains_Allowed(string host)
    {
        Policy().IsAllowed(host).ShouldBeTrue();
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("169.254.169.254")]                 // cloud metadata literal — SsrfGuard also blocks, allowlist rejects first
    [InlineData("notgithub.com")]                    // not a subdomain of github.com (no leading dot)
    [InlineData("github.com.evil.com")]              // suffix-attack: github.com is a label, not the host's tail-domain
    public void IsAllowed_OffListHosts_Rejected(string host)
    {
        Policy().IsAllowed(host).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_ConfiguredHost_AllowedAlongsideDefaults()
    {
        var policy = Policy("github.mycorp.com");

        policy.IsAllowed("github.mycorp.com").ShouldBeTrue();      // configured
        policy.IsAllowed("ci.github.mycorp.com").ShouldBeTrue();   // subdomain of configured
        policy.IsAllowed("github.com").ShouldBeTrue();             // defaults still intact
        policy.IsAllowed("other.example.com").ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_ConfiguredEntryAsUrl_NormalizedToHost()
    {
        Policy("https://ado.mycorp.com:8443/tfs").IsAllowed("ado.mycorp.com").ShouldBeTrue();
    }
}
