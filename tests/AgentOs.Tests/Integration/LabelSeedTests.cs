// Bootstrap (slice 1) — the label-seed write path: the pure diff (LabelSync.Partition, no live
// GitHub), the standard taxonomy's invariants, the BoardWriteService pass-through to the resolved
// provider, and the Azure DevOps stub's honest NotSupported.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Integration.Sources;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class LabelSeedTests
{
    [Fact]
    public void Partition_SkipsExistingCaseInsensitive_AndCreatesMissing()
    {
        LabelSpec[] desired =
        [
            new("type:feature", "1f883d"),
            new("ai:ready", "8957e5"),
            new("p1", "d4a72c"),
        ];
        string[] existing = ["TYPE:FEATURE", "p1"]; // different case + exact match

        var (toCreate, present) = LabelSync.Partition(existing, desired);

        toCreate.Select(l => l.Name).ShouldBe(["ai:ready"]);
        present.ShouldBe(["type:feature", "p1"], ignoreOrder: true);
    }

    [Fact]
    public void Partition_EmptyRepo_CreatesAll()
    {
        var (toCreate, present) = LabelSync.Partition([], StandardLabels.All);

        toCreate.Count.ShouldBe(StandardLabels.All.Count);
        present.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1f883d", "1f883d")]
    [InlineData("#1f883d", "1f883d")]
    public void NormalizeColor_StripsLeadingHash(string input, string expected)
        => LabelSync.NormalizeColor(input).ShouldBe(expected);

    [Fact]
    public void StandardLabels_AreWellFormed()
    {
        var all = StandardLabels.All;

        all.ShouldNotBeEmpty();
        all.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            .ShouldBe(all.Count, "label names must be unique");
        all.ShouldContain(l => l.Name == StandardLabels.AiReady);
        all.ShouldContain(l => l.Name == StandardLabels.NeedsHuman);

        foreach (var label in all)
        {
            label.Color.Length.ShouldBe(6, $"{label.Name} color must be 6 hex chars");
            label.Color.ShouldNotStartWith("#");
            label.Color.All(Uri.IsHexDigit).ShouldBeTrue($"{label.Name} color '{label.Color}' must be hex");
        }
    }

    [Fact]
    public async Task BoardWriteService_EnsureLabels_DelegatesToResolvedProvider()
    {
        var provider = Substitute.For<ISourceProvider>();
        provider.Kind.Returns(SourceProviderKind.GitHub);
        var resolver = Substitute.For<ISourceProviderResolver>();
        resolver.TryResolve(SourceProviderKind.GitHub, out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = provider; return true; });

        var repo = new WorkspaceDescriptor(
            Guid.NewGuid(), "t1", SourceProviderKind.GitHub, "octo", "api", null, "main", "tok");
        var expected = new LabelSyncResult(["type:bug"], ["p1"]);
        provider.EnsureLabelsAsync(repo, Arg.Any<IReadOnlyList<LabelSpec>>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var svc = new BoardWriteService(resolver);
        var result = await svc.EnsureLabelsAsync(repo, StandardLabels.All);

        result.Created.ShouldBe(["type:bug"]);
        result.Existing.ShouldBe(["p1"]);
    }

    [Fact]
    public async Task BoardWriteService_NoProvider_Throws()
    {
        var resolver = Substitute.For<ISourceProviderResolver>();
        resolver.TryResolve(Arg.Any<SourceProviderKind>(), out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = null; return false; });
        var repo = new WorkspaceDescriptor(
            Guid.NewGuid(), "t1", SourceProviderKind.AzureDevOps, "org", "repo", "proj", "main", "tok");

        var svc = new BoardWriteService(resolver);

        await Should.ThrowAsync<InvalidOperationException>(() => svc.EnsureLabelsAsync(repo, StandardLabels.All));
    }

    [Fact]
    public async Task AzureDevOps_EnsureLabels_NotSupported()
    {
        var provider = new AzureDevOpsSourceProvider();
        var repo = new WorkspaceDescriptor(
            Guid.NewGuid(), "t1", SourceProviderKind.AzureDevOps, "org", "repo", "proj", "main", "tok");

        await Should.ThrowAsync<NotSupportedException>(() => provider.EnsureLabelsAsync(repo, StandardLabels.All));
    }

    [Fact]
    public async Task BoardWriteService_CreateTickets_DelegatesToResolvedProvider()
    {
        var provider = Substitute.For<ISourceProvider>();
        provider.Kind.Returns(SourceProviderKind.GitHub);
        var resolver = Substitute.For<ISourceProviderResolver>();
        resolver.TryResolve(SourceProviderKind.GitHub, out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = provider; return true; });

        var board = new BoardDescriptor(Guid.NewGuid(), "t1", SourceProviderKind.GitHub, "octo", "org", 5, "node", "tok");
        var repo = new WorkspaceDescriptor(Guid.NewGuid(), "t1", SourceProviderKind.GitHub, "octo", "api", null, "main", "tok");
        var drafts = new[] { new TicketDraft("T", "B", ["type:feature"], true) };
        IReadOnlyList<CreatedTicket> expected = [new CreatedTicket(1, "n", "https://x/1", "item1", "T")];
        provider.CreateTicketsAsync(board, repo, Arg.Any<IReadOnlyList<TicketDraft>>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var svc = new BoardWriteService(resolver);
        var result = await svc.CreateTicketsAsync(board, repo, drafts);

        result.Count.ShouldBe(1);
        result[0].Number.ShouldBe(1);
        result[0].ItemNodeId.ShouldBe("item1");
    }

    [Fact]
    public async Task AzureDevOps_CreateTickets_NotSupported()
    {
        var provider = new AzureDevOpsSourceProvider();
        var board = new BoardDescriptor(Guid.NewGuid(), "t1", SourceProviderKind.AzureDevOps, "org", "org", null, null, "tok", "proj");
        var repo = new WorkspaceDescriptor(Guid.NewGuid(), "t1", SourceProviderKind.AzureDevOps, "org", "repo", "proj", "main", "tok");

        await Should.ThrowAsync<NotSupportedException>(() => provider.CreateTicketsAsync(board, repo, Array.Empty<TicketDraft>()));
    }
}
