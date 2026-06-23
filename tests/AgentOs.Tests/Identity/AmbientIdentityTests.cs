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
}
