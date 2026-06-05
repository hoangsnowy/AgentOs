// Shared runner-provisioning path: mint a pairing token + persist a runner row. Used by BOTH the
// /runners HTTP endpoint (member pastes the token) and the VS Code browser-pairing flow (the extension
// receives the token via a one-time code) so there is one place that creates a runner.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Sessions.Persistence;
using AgentOs.Modules.Sessions.Persistence.Entities;

namespace AgentOs.Modules.Sessions.Pairing;

/// <summary>A freshly provisioned runner: its id + the one-time plaintext pairing token.</summary>
internal sealed record ProvisionedRunner(Guid RunnerId, string Label, string Token, string Status);

/// <summary>Creates a runner row and its pairing secret. Tenant + owner are passed explicitly so the
/// service works from an HTTP request (ITenantContext) and any other caller alike.</summary>
internal interface IRunnerProvisioningService
{
    Task<ProvisionedRunner> ProvisionAsync(string tenantId, string userId, string label, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class RunnerProvisioningService : IRunnerProvisioningService
{
    private readonly IRunnerRepository _repo;
    private readonly IRunnerPairingService _pairing;
    private readonly TimeProvider _clock;

    public RunnerProvisioningService(IRunnerRepository repo, IRunnerPairingService pairing, TimeProvider clock)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<ProvisionedRunner> ProvisionAsync(string tenantId, string userId, string label, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        // 256-bit token; only its salted hash is persisted (the plaintext is returned once, never stored).
        var secret = _pairing.Issue();
        var entity = new RunnerEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerUserId = userId,
            Label = label.Trim(),
            TokenHash = secret.TokenHash,
            Status = "Pending",
            CreatedAtUtc = _clock.GetUtcNow(),
            CreatedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
        };
        await _repo.AddAsync(entity, ct).ConfigureAwait(false);

        return new ProvisionedRunner(entity.Id, entity.Label, secret.Token, entity.Status);
    }
}
