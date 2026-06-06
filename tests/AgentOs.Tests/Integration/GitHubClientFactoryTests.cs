// The shared GitHub client factory both PR paths + the source provider now use: public vs Enterprise
// host, authenticated vs anonymous.

using System;
using AgentOs.Modules.Integration;
using Octokit;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class GitHubClientFactoryTests
{
    [Fact]
    public void Create_NoHost_TargetsPublicGitHub()
    {
        var client = GitHubClientFactory.Create("tok");
        client.BaseAddress!.Host.ShouldBe("api.github.com");
    }

    [Fact]
    public void Create_WithHost_TargetsEnterprise()
    {
        var client = GitHubClientFactory.Create("tok", "https://ghe.acme.com");
        client.BaseAddress!.Host.ShouldBe("ghe.acme.com");
    }

    [Fact]
    public void Create_WithToken_IsAuthenticated()
    {
        GitHubClientFactory.Create("tok").Credentials.AuthenticationType.ShouldBe(AuthenticationType.Oauth);
    }

    [Fact]
    public void Create_NoToken_IsAnonymous()
    {
        GitHubClientFactory.Create(null).Credentials.AuthenticationType.ShouldBe(AuthenticationType.Anonymous);
    }
}
