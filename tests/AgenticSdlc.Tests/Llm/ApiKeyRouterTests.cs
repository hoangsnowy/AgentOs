// AgenticSdlc.Tests/Llm/ApiKeyRouterTests.cs
// Unit tests for the multi-key router: round-robin selection + rate-limit (429) failover via cooldown.

using System;
using System.Collections.Generic;
using AgenticSdlc.Infrastructure.Llm;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class ApiKeyRouterTests
{
    private static ApiKeyRouter NewRouter() => new(TimeProvider.System);

    [Fact]
    public void Acquire_MultipleKeys_RoundRobinsInOrder()
    {
        var router = NewRouter();
        var keys = new List<string> { "a", "b", "c" };

        router.Acquire("P", keys).ShouldBe("a");
        router.Acquire("P", keys).ShouldBe("b");
        router.Acquire("P", keys).ShouldBe("c");
        router.Acquire("P", keys).ShouldBe("a");
    }

    [Fact]
    public void Acquire_AfterPenalize_SkipsCooledKey()
    {
        var router = NewRouter();
        var keys = new List<string> { "a", "b", "c" };

        router.Penalize("P", "a"); // cools "a" for the default 60s window

        var picks = new[] { router.Acquire("P", keys), router.Acquire("P", keys), router.Acquire("P", keys) };

        picks.ShouldNotContain("a");
        picks.ShouldContain("b");
        picks.ShouldContain("c");
    }

    [Fact]
    public void Acquire_AllPenalized_StillReturnsAKey()
    {
        var router = NewRouter();
        var keys = new List<string> { "a", "b" };

        router.Penalize("P", "a");
        router.Penalize("P", "b");

        router.Acquire("P", keys).ShouldNotBeNull(); // best-effort: still attempts the soonest-to-recover
    }

    [Fact]
    public void Acquire_EmptyPool_ReturnsNull()
        => NewRouter().Acquire("P", new List<string>()).ShouldBeNull();

    [Fact]
    public void Acquire_DifferentProviders_DoNotShareCursor()
    {
        var router = NewRouter();
        var keys = new List<string> { "a", "b" };

        router.Acquire("Claude", keys).ShouldBe("a");
        router.Acquire("AzureOpenAI", keys).ShouldBe("a"); // independent round-robin per provider
    }

    [Fact]
    public void AvailableCount_ReflectsCooldowns()
    {
        var router = NewRouter();
        var keys = new List<string> { "a", "b", "c" };

        router.AvailableCount("P", keys).ShouldBe(3);
        router.Penalize("P", "a");
        router.AvailableCount("P", keys).ShouldBe(2);
    }
}
