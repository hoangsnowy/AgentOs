// Unit tests for RunnerProvisioningService — the shared "create a runner + issue its token" path used by
// both the /runners endpoint and the VS Code browser-pairing flow.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Sessions.Pairing;
using AgentOs.Modules.Sessions.Persistence;
using AgentOs.Modules.Sessions.Persistence.Entities;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public class RunnerProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_IssuesToken_PersistsPendingRunner_StoresOnlyHash()
    {
        var repo = Substitute.For<IRunnerRepository>();
        RunnerEntity? saved = null;
        repo.AddAsync(Arg.Do<RunnerEntity>(e => saved = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var pairing = new RunnerPairingService(); // real crypto — issues a genuine token + salted hash

        var svc = new RunnerProvisioningService(repo, pairing, TimeProvider.System);
        var result = await svc.ProvisionAsync("tenant-a", "user-1", "  Hoang laptop  ");

        result.Token.ShouldNotBeNullOrWhiteSpace();
        result.Status.ShouldBe("Pending");
        result.Label.ShouldBe("Hoang laptop");          // trimmed
        result.RunnerId.ShouldNotBe(Guid.Empty);

        saved.ShouldNotBeNull();
        saved!.TenantId.ShouldBe("tenant-a");
        saved.OwnerUserId.ShouldBe("user-1");
        saved.Status.ShouldBe("Pending");
        saved.TokenHash.ShouldNotBe(result.Token);                  // plaintext is never persisted
        pairing.Verify(result.Token, saved.TokenHash).ShouldBeTrue(); // the stored hash matches the token
    }

    [Fact]
    public async Task ProvisionAsync_BlankLabel_Throws()
    {
        var svc = new RunnerProvisioningService(
            Substitute.For<IRunnerRepository>(), new RunnerPairingService(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentException>(() => svc.ProvisionAsync("t", "u", "   "));
    }
}
