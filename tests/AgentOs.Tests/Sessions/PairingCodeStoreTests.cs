// Unit tests for PairingCodeStore — the one-time, short-lived exchange codes that carry a runner's
// credentials from the browser approve step to the editor extension (the token never rides a URL).

using System;
using AgentOs.Modules.Sessions.Pairing;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public class PairingCodeStoreTests
{
    private static PairingPayload Payload() =>
        new(Guid.NewGuid(), "tok-abc", "https://localhost:5180/hubs/remote-agent");

    [Fact]
    public void Stash_ThenRedeem_ReturnsPayload_AndIsSingleUse()
    {
        var store = new PairingCodeStore(TimeProvider.System);
        var payload = Payload();

        var code = store.Stash(payload);
        var first = store.Redeem(code);

        first.ShouldNotBeNull();
        first!.RunnerId.ShouldBe(payload.RunnerId);
        first.Token.ShouldBe("tok-abc");
        first.HubUrl.ShouldBe(payload.HubUrl);

        store.Redeem(code).ShouldBeNull(); // already consumed
    }

    [Fact]
    public void Redeem_UnknownCode_IsNull()
    {
        new PairingCodeStore(TimeProvider.System).Redeem("not-a-real-code").ShouldBeNull();
    }

    [Fact]
    public void Redeem_AfterTtl_IsNull()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var store = new PairingCodeStore(clock);

        var code = store.Stash(Payload());
        clock.Advance(PairingCodeStore.Ttl + TimeSpan.FromSeconds(1));

        store.Redeem(code).ShouldBeNull();
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
