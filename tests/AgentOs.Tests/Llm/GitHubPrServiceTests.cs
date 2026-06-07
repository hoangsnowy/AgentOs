// Pre-flight config checks for GitHubPrService. The Octokit-driven path needs a live (or
// fake-HTTP) GitHub server, so we only cover the synchronous validation branches here — a real
// PR open is exercised by the smoke / integration tier.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Pipeline;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class GitHubPrServiceTests
{
    [Fact]
    public async Task OpenPrAsync_MissingPat_ThrowsWithClearMessage()
    {
        var overrides = Substitute.For<IRuntimeOverrides>();
        overrides.GitHubPat.ReturnsNull();
        overrides.GitHubRepoOwner.Returns("hoangsnowy");
        overrides.GitHubRepoName.Returns("AgentOs");

        var svc = new GitHubPrService(overrides, NullLogger<GitHubPrService>.Instance);
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await svc.OpenPrAsync(null!, "title", "body", CancellationToken.None));
        ex.Message.ShouldContain("GitHubPat");
        ex.Message.ShouldContain("workspace");
    }

    [Fact]
    public async Task OpenPrAsync_MissingOwnerAndName_ListsBothInMessage()
    {
        var overrides = Substitute.For<IRuntimeOverrides>();
        overrides.GitHubPat.Returns("ghp_x");
        overrides.GitHubRepoOwner.ReturnsNull();
        overrides.GitHubRepoName.Returns("   ");

        var svc = new GitHubPrService(overrides, NullLogger<GitHubPrService>.Instance);
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await svc.OpenPrAsync(null!, "title", "body", CancellationToken.None));
        ex.Message.ShouldContain("GitHubRepoOwner");
        ex.Message.ShouldContain("GitHubRepoName");
    }

    [Fact]
    public async Task OpenPrAsync_BlankTitle_ThrowsArgumentException()
    {
        var overrides = Substitute.For<IRuntimeOverrides>();
        var svc = new GitHubPrService(overrides, NullLogger<GitHubPrService>.Instance);
        await Should.ThrowAsync<ArgumentException>(async () =>
            await svc.OpenPrAsync(null!, "  ", "body", CancellationToken.None));
    }
}
