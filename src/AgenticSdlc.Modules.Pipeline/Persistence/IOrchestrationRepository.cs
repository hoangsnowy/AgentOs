// Repository for the Agent Studio orchestration graph. Stored as a JSON string (the Web layer
// (de)serializes it to its own OrchestrationGraph) → Application/Infrastructure do not depend on Web.
namespace AgenticSdlc.Modules.Pipeline.Persistence;

/// <summary>CRUD for the orchestration definition (Agent Studio editor state).</summary>
public interface IOrchestrationRepository
{
    Task<IReadOnlyList<OrchestrationRecord>> ListAsync(CancellationToken ct = default);

    Task<OrchestrationRecord?> GetAsync(string id, CancellationToken ct = default);

    Task UpsertAsync(OrchestrationRecord record, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>A single orchestration graph (DefinitionJson = full graph serialized by the Web layer).</summary>
public sealed record OrchestrationRecord(
    string Id,
    string Name,
    string? Description,
    string DefinitionJson,
    DateTimeOffset UpdatedAtUtc);
