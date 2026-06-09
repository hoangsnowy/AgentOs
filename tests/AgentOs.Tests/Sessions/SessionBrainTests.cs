// Coherence Phase 2 (A2a) — the session Brain ("Quick"/"Quality") + captured TicketType persist and
// round-trip through the EF model (the AddSessionBrain migration's columns), and a fresh session defaults
// to the Quick engine. EF Core InMemory; reads go through the DbContext (the SessionRepository read-side is
// Dapper-only and needs a real Postgres). The query filter is instance-member based (C1) so building this
// context model alongside other Sessions tests is safe.

using AgentOs.Modules.Sessions.Persistence;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public sealed class SessionBrainTests
{
    private sealed class FakeTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "m";
        public string? UserName => "m";
        public IReadOnlyList<string> Roles { get; } = new[] { "admin" };
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    [Fact]
    public async Task AddForTenant_PersistsBrainAndTicketType_RoundTrip()
    {
        var tenant = new FakeTenant("t1");
        var options = new DbContextOptionsBuilder<SessionsDbContext>()
            .UseInMemoryDatabase($"sessions-brain-{Guid.NewGuid()}")
            .Options;
        var id = Guid.NewGuid();

        await using (var db = new SessionsDbContext(options, tenant))
        {
            db.Sessions.Add(new RemoteSessionEntity
            {
                Id = id,
                TenantId = "t1",
                WorkspaceId = Guid.NewGuid(),
                MemberUserId = "m",
                Title = "Add export",
                Status = "Draft",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Brain = "Quality",
                TicketType = "type:feature",
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new SessionsDbContext(options, tenant))
        {
            var session = await db.Sessions.FirstAsync(s => s.Id == id);
            session.Brain.ShouldBe("Quality");
            session.TicketType.ShouldBe("type:feature");
        }
    }

    [Fact]
    public void NewSession_DefaultsToQuickBrain_NoTicketType()
    {
        var session = new RemoteSessionEntity();
        session.Brain.ShouldBe("Quick");
        session.TicketType.ShouldBeNull();
    }
}
