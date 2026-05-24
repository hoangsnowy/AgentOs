// Persistence entity for the Agent Studio orchestration graph (replaces App_Data/orchestrations.json).
namespace AgenticSdlc.Infrastructure.Persistence.Entities;

internal sealed class OrchestrationEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Full OrchestrationGraph serialized as JSON (jsonb column).</summary>
    public string DefinitionJson { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
