// Honest degraded mode. Without ConnectionStrings:DefaultConnection the modules swap in no-op
// repositories so the host still boots — but a UI that "saves" into a no-op repo fake-succeeds
// (a registered runner vanishes on reload). Apps inject this to render a "requires a database"
// notice instead of silently no-oping.

namespace AgentOs.SharedKernel.Persistence;

/// <summary>Whether a real database is wired (vs the stateless no-op-repository boot).</summary>
public sealed record DatabaseAvailability(bool IsConfigured);
