// AgenticSdlc.Infrastructure/Validation/DependencyInjection.cs
// Sprint 3 — DI cho ILlmOutputValidator. Đăng ký 3 schema từ embedded resource.

using System;
using System.IO;
using System.Reflection;
using AgenticSdlc.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Infrastructure.Validation;

/// <summary>DI extension cho validation layer.</summary>
public static class ValidationServiceCollectionExtensions
{
    private const string ResourcePrefix = "AgenticSdlc.Infrastructure.Schemas.";

    /// <summary>Đăng ký <see cref="JsonSchemaValidator"/> singleton + load 3 schema từ embedded resource.</summary>
    public static IServiceCollection AddValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validator = new JsonSchemaValidator();
        var asm = typeof(JsonSchemaValidator).Assembly;
        validator.RegisterFromJson(SchemaNames.RequirementSpecV1, ReadResource(asm, "requirement-spec.v1.json"));
        validator.RegisterFromJson(SchemaNames.CodeArtifactV1, ReadResource(asm, "code-artifact.v1.json"));
        validator.RegisterFromJson(SchemaNames.TestArtifactV1, ReadResource(asm, "test-artifact.v1.json"));

        services.AddSingleton<ILlmOutputValidator>(validator);
        return services;
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        var resourceName = ResourcePrefix + name;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Schema embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
