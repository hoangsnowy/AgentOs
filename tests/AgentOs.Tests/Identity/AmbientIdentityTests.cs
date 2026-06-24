// Unit tests for AmbientIdentity — the AsyncLocal (tenant, user) override used by background work
// (a Blazor circuit's Task.Run) where the request-scoped ITenantContext resolves blank.

using System.Threading.Tasks;
using AgentOs.SharedKernel.Identity;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Identity;

public class AmbientIdentityTests
{
    [Fact]
    public void Current_WhenNothingPushed_IsNull()
    {
        AmbientIdentity.Current.ShouldBeNull();
    }

    [Fact]
    public void Push_SetsCurrent_DisposeRestores()
    {
        using (AmbientIdentity.Push("tenant-a", "alice"))
        {
            AmbientIdentity.Current.ShouldNotBeNull();
            AmbientIdentity.Current!.TenantId.ShouldBe("tenant-a");
            AmbientIdentity.Current.UserId.ShouldBe("alice");
        }

        AmbientIdentity.Current.ShouldBeNull();
    }

    [Fact]
    public void Push_Nested_RestoresOuterOnInnerDispose()
    {
        using (AmbientIdentity.Push("outer", "o"))
        {
            using (AmbientIdentity.Push("inner", "i"))
            {
                AmbientIdentity.Current!.TenantId.ShouldBe("inner");
            }

            AmbientIdentity.Current!.TenantId.ShouldBe("outer");
        }
    }

    [Fact]
    public void Push_NullUser_Allowed()
    {
        using (AmbientIdentity.Push("tenant-a", null))
        {
            AmbientIdentity.Current!.UserId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Current_FlowsAcrossAwait()
    {
        using (AmbientIdentity.Push("tenant-a", "alice"))
        {
            await Task.Yield();
            AmbientIdentity.Current!.TenantId.ShouldBe("tenant-a");
        }
    }

    [Fact]
    public void PushOrNull_NonBlankTenant_PushesAndReturnsHandle()
    {
        using (var handle = AmbientIdentity.PushOrNull("tenant-a", "alice"))
        {
            handle.ShouldNotBeNull();
            AmbientIdentity.Current!.TenantId.ShouldBe("tenant-a");
        }

        AmbientIdentity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PushOrNull_BlankTenant_ReturnsNull_AndDoesNotPush(string? blank)
    {
        var handle = AmbientIdentity.PushOrNull(blank, "alice");

        handle.ShouldBeNull();              // safe to `using var _ = …;` — null disposes to a no-op
        AmbientIdentity.Current.ShouldBeNull();
    }

    private sealed class StubContext(string tenantId, string? userId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
        public string? UserId { get; } = userId;
        public string? UserName => null;
        public IReadOnlyList<string> Roles => [];
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    [Fact]
    public void Resolve_ExplicitTenant_WinsOverAmbientAndContext()
    {
        using (AmbientIdentity.Push("ambient-t", "ambient-u"))
        {
            var id = AmbientIdentity.Resolve("explicit-t", "explicit-u", new StubContext("ctx-t", "ctx-u"));
            id.TenantId.ShouldBe("explicit-t");
            id.UserId.ShouldBe("explicit-u");
        }
    }

    [Fact]
    public void Resolve_NoExplicit_AmbientBeatsContext()
    {
        using (AmbientIdentity.Push("ambient-t", "ambient-u"))
        {
            var id = AmbientIdentity.Resolve(explicitTenantId: null, explicitUserId: null, new StubContext("ctx-t", "ctx-u"));
            id.TenantId.ShouldBe("ambient-t");
            id.UserId.ShouldBe("ambient-u");
        }
    }

    [Fact]
    public void Resolve_NoExplicitNoAmbient_UsesContext()
    {
        var id = AmbientIdentity.Resolve(null, null, new StubContext("ctx-t", "ctx-u"));
        id.TenantId.ShouldBe("ctx-t");
        id.UserId.ShouldBe("ctx-u");
    }

    [Fact]
    public void Resolve_ContextEmptyTenant_StaysEmpty_FailClosed_NotDefault()
    {
        // An authenticated-but-no-tenant context returns "" — Resolve must NOT silently promote it to
        // `default` (that would leak the work to the default tenant). Only a MISSING context defaults.
        var id = AmbientIdentity.Resolve(null, null, new StubContext("", null));
        id.TenantId.ShouldBe("");
    }

    [Fact]
    public void Resolve_NothingAvailable_FallsToDefaultTenant()
    {
        var id = AmbientIdentity.Resolve(explicitTenantId: "  ", explicitUserId: null, context: null);
        id.TenantId.ShouldBe(ITenantContext.DefaultTenantId);
        id.UserId.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ExplicitBlank_DefersToAmbient()
    {
        // OrchestrationStudio passes "" under standalone dev-login — a blank explicit must fall through.
        using (AmbientIdentity.Push("ambient-t", null))
        {
            AmbientIdentity.Resolve("", null, null).TenantId.ShouldBe("ambient-t");
        }
    }
}
