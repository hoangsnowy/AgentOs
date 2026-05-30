// M2 — unit tests for the shared connect-a-workspace flow (validate → store credential → persist),
// exercised tenant-explicitly so it works from both the HTTP endpoint and the desktop circuit.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Workspaces;
using AgentOs.Modules.Workspaces.Persistence;
using AgentOs.Modules.Workspaces.Persistence.Entities;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Workspaces;

public sealed class WorkspaceConnectorTests
{
    private readonly IWorkspaceRepository _repo = Substitute.For<IWorkspaceRepository>();
    private readonly ISourceProvider _provider = Substitute.For<ISourceProvider>();
    private readonly ISourceProviderResolver _resolver = Substitute.For<ISourceProviderResolver>();
    private readonly InMemoryAppConfigStore _credentials = new();

    private WorkspaceConnector Sut()
    {
        _provider.Kind.Returns(SourceProviderKind.GitHub);
        _resolver.TryResolve(SourceProviderKind.GitHub, out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = _provider; return true; });
        return new WorkspaceConnector(_repo, _resolver, _credentials, TimeProvider.System);
    }

    private static WorkspaceConnectInput GitHubInput(string token = "ghp_x") =>
        new("my-svc", SourceProviderKind.GitHub, "octocat", "hello", null, "main", null, token);

    [Fact]
    public async Task ConnectAsync_ValidRepo_PersistsWorkspace_AndStoresEncryptedToken()
    {
        var sut = Sut();
        _provider.ValidateAsync(Arg.Any<WorkspaceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(RepoValidation.Success("develop"));

        var result = await sut.ConnectAsync("tenant-1", "user-1", GitHubInput());

        result.Ok.ShouldBeTrue();
        result.Workspace.ShouldNotBeNull();
        result.Workspace!.TenantId.ShouldBe("tenant-1");
        result.Workspace.DefaultBranch.ShouldBe("develop");           // taken from the validation result
        result.Workspace.CredentialRef.ShouldStartWith("workspace/"); // row keeps only the reference
        await _repo.Received(1).AddForTenantAsync(Arg.Any<WorkspaceEntity>(), Arg.Any<CancellationToken>());
        // Token landed in the (encrypted in prod) credential store under the workspace's ref.
        (await _credentials.GetAsync(result.Workspace.CredentialRef)).ShouldBe("ghp_x");
    }

    [Fact]
    public async Task ConnectAsync_ProviderValidationFails_ReturnsError_AndPersistsNothing()
    {
        var sut = Sut();
        _provider.ValidateAsync(Arg.Any<WorkspaceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(RepoValidation.Fail("bad token"));

        var result = await sut.ConnectAsync("tenant-1", "user-1", GitHubInput());

        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("bad token");
        await _repo.DidNotReceive().AddForTenantAsync(Arg.Any<WorkspaceEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectAsync_MissingRequiredFields_ReturnsError()
    {
        var sut = Sut();
        var result = await sut.ConnectAsync("tenant-1", "user-1", GitHubInput(token: ""));
        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConnectAsync_NoProviderForKind_ReturnsError()
    {
        var resolver = Substitute.For<ISourceProviderResolver>();
        resolver.TryResolve(Arg.Any<SourceProviderKind>(), out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = null; return false; });
        var sut = new WorkspaceConnector(_repo, resolver, _credentials, TimeProvider.System);

        var result = await sut.ConnectAsync("tenant-1", "user-1", GitHubInput());

        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("provider");
    }
}
