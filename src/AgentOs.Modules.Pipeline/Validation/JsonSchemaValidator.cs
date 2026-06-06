// AgentOs.Infrastructure/Validation/JsonSchemaValidator.cs
// Sprint 3 — ILlmOutputValidator impl using JsonSchema.Net 9.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AgentOs.Modules.Pipeline.Validation;
using Json.Schema;

namespace AgentOs.Modules.Pipeline.Validation;

/// <summary>JSON Schema 2020-12 validator. Loads schemas from a file path at startup, caches by name.</summary>
public sealed class JsonSchemaValidator : ILlmOutputValidator
{
    private readonly ConcurrentDictionary<string, JsonSchema> _schemas = new();
    private readonly EvaluationOptions _options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = false,
    };

    // v9 registers each schema's $id into the SchemaRegistry at build time and refuses to overwrite
    // an existing entry. Build into a per-instance registry (reads still fall back to Global for the
    // meta-schemas) so registering the same $id from more than one validator — multiple AddValidation()
    // calls across tests/hosts — never collides in the global registry.
    private readonly BuildOptions _buildOptions = new() { SchemaRegistry = new SchemaRegistry() };

    /// <summary>Registers a schema from a file path. Call once at startup.</summary>
    public void RegisterFromFile(string schemaName, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Schema file not found: {filePath}", filePath);
        }
        var text = File.ReadAllText(filePath);
        var schema = JsonSchema.FromText(text, _buildOptions);
        _schemas[schemaName] = schema;
    }

    /// <summary>Registers a schema from a raw JSON string.</summary>
    public void RegisterFromJson(string schemaName, string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        _schemas[schemaName] = JsonSchema.FromText(schemaJson, _buildOptions);
    }

    /// <inheritdoc />
    public void Validate(string json, string schemaName, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (!_schemas.TryGetValue(schemaName, out var schema))
        {
            throw new InvalidOperationException($"Schema '{schemaName}' has not been registered.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LlmOutputValidationException(
                agentName, schemaName,
                new[] { $"$: JSON parse error — {ex.Message}" });
        }

        // JsonSchema.Net 9 evaluates a read-only JsonElement (no JsonNode allocation).
        using (doc)
        {
            var result = schema.Evaluate(doc.RootElement, _options);
            if (result.IsValid)
            {
                return;
            }

            var errors = Flatten(result).ToList();
            if (errors.Count == 0)
            {
                errors.Add("$: schema validation failed (no detail).");
            }
            throw new LlmOutputValidationException(agentName, schemaName, errors);
        }
    }

    private static IEnumerable<string> Flatten(EvaluationResults results)
    {
        // v9: HasErrors/HasDetails removed — check the collections directly. InstanceLocation is a
        // non-nullable JsonPointer struct (root renders as an empty string).
        if (results.Errors is not null)
        {
            foreach (var (key, message) in results.Errors)
            {
                var path = results.InstanceLocation.ToString();
                if (string.IsNullOrEmpty(path))
                {
                    path = "$";
                }
                yield return $"{path}: [{key}] {message}";
            }
        }
        if (results.Details is null)
        {
            yield break;
        }
        foreach (var detail in results.Details)
        {
            foreach (var sub in Flatten(detail))
            {
                yield return sub;
            }
        }
    }
}
