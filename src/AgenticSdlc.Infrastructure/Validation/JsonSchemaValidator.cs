// AgenticSdlc.Infrastructure/Validation/JsonSchemaValidator.cs
// Sprint 3 — impl ILlmOutputValidator bằng JsonSchema.Net 7.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgenticSdlc.Application.Validation;
using Json.Schema;

namespace AgenticSdlc.Infrastructure.Validation;

/// <summary>JSON Schema 2020-12 validator. Load schema từ file path lúc startup, cache theo tên.</summary>
public sealed class JsonSchemaValidator : ILlmOutputValidator
{
    private readonly ConcurrentDictionary<string, JsonSchema> _schemas = new();
    private readonly EvaluationOptions _options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = false,
    };

    /// <summary>Đăng ký schema từ file path. Gọi 1 lần lúc startup.</summary>
    public void RegisterFromFile(string schemaName, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Schema file not found: {filePath}", filePath);
        }
        var text = File.ReadAllText(filePath);
        var schema = JsonSchema.FromText(text);
        _schemas[schemaName] = schema;
    }

    /// <summary>Đăng ký schema từ raw JSON string.</summary>
    public void RegisterFromJson(string schemaName, string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        _schemas[schemaName] = JsonSchema.FromText(schemaJson);
    }

    /// <inheritdoc />
    public void Validate(string json, string schemaName, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (!_schemas.TryGetValue(schemaName, out var schema))
        {
            throw new InvalidOperationException($"Schema '{schemaName}' chưa được đăng ký.");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LlmOutputValidationException(
                agentName, schemaName,
                new[] { $"$: JSON parse error — {ex.Message}" });
        }

        var result = schema.Evaluate(node, _options);
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

    private static IEnumerable<string> Flatten(EvaluationResults results)
    {
        if (!results.IsValid && results.HasErrors && results.Errors is not null)
        {
            foreach (var (key, message) in results.Errors)
            {
                var path = results.InstanceLocation?.ToString() ?? "$";
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
