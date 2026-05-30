// Single-tenant stub for persistence tests — fills the slot the deleted DefaultTenantContext used
// to fill so per-tenant DbContexts can be exercised without spinning up an HttpContext + cookie.

using System.Collections.Generic;
using AgentOs.SharedKernel.Identity;

namespace AgentOs.Tests.Identity;

internal sealed class TestTenantContext : ITenantContext
{
    public string TenantId => ITenantContext.DefaultTenantId;
    public string? UserId => "operator";
    public string? UserName => "operator";
    public IReadOnlyList<string> Roles { get; } = new[] { "admin" };
    public bool IsAuthenticated => false;
    public bool IsAdmin => true;
}
