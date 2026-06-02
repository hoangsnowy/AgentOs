---
name: agent-scaffold
description: >
  Scaffold a new pipeline agent in AgentOs.Modules.Pipeline following the modular monolith
  layout: contract + impl in the same module, ILlmClient consumption via ILlmClientFactory
  (Domain), DI registration via PipelineModule.AddAgents, xUnit test stub with NSubstitute
  ILlmClient mock. Use when the user says "scaffold agent X", "add agent X", "new agent",
  or invokes "/agent-scaffold X".
---

Scaffold one pipeline agent end-to-end inside `AgentOs.Modules.Pipeline`.

## Input

1. **Name** (PascalCase, no `Agent` suffix). Ex: `Security`, `Doc`, `Reviewer`.
2. **Provider**: `Codex` | `AzureOpenAI` | `MAF` | `RemoteAgent`.
3. **Model**: e.g. `Codex-sonnet-4-20250514`, `gpt-4.1`.
4. **Input + Output shape**: records. JSON output → declare schema.
5. **System prompt** (or `TODO` stub).

## Output

### 1. `src/AgentOs.Modules.Pipeline/Agents/I{Name}Agent.cs`

```csharp
namespace AgentOs.Modules.Pipeline.Agents;

public interface I{Name}Agent
{
    Task<{Name}Result> RunAsync({Name}Input input, CancellationToken ct = default);
}

public sealed record {Name}Input(/* fields */);
public sealed record {Name}Result(/* fields */, decimal CostUsd, int InputTokens, int OutputTokens);
```

### 2. `src/AgentOs.Modules.Pipeline/Agents/{Name}Agent.cs`

```csharp
using AgentOs.Domain.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.Agents;

public sealed class {Name}Agent : I{Name}Agent
{
    private const string SystemPrompt = """
        {SystemPrompt or TODO}
        """;

    private readonly ILlmClient _llm;
    private readonly AgentOptions _options;
    private readonly ILogger<{Name}Agent> _logger;

    public {Name}Agent(ILlmClientFactory factory, IOptions<AgentsOptions> options, ILogger<{Name}Agent> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.{Name} ?? throw new InvalidOperationException("Agents:{Name} not configured.");
        _llm = factory.Create(_options.Provider);
        _logger = logger;
    }

    public async Task<{Name}Result> RunAsync({Name}Input input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = new LlmRequest(SystemPrompt, BuildUserPrompt(input), _options.Model, _options.Temperature, _options.MaxTokens);
        var response = await _llm.SendAsync(request, ct).ConfigureAwait(false);

        _logger.LogInformation("{Agent} done: {InTok}->{OutTok} tokens, ${Cost}, {Ms}ms",
            nameof({Name}Agent), response.InputTokens, response.OutputTokens, response.CostUsd, response.Latency.TotalMilliseconds);

        return Parse(response);
    }

    private static string BuildUserPrompt({Name}Input input) => $"TODO: format {input}";

    private static {Name}Result Parse(LlmResponse r)
        => new(/* parse r.Content */, r.CostUsd, r.InputTokens, r.OutputTokens);
}
```

### 3. Edit `src/AgentOs.Modules.Pipeline/Agents/DependencyInjection.cs`

Add `services.AddTransient<I{Name}Agent, {Name}Agent>();` inside the existing `AddAgents` extension (called from `PipelineModule.AddServices`).

### 4. Edit `src/AgentOs.Api/appsettings.json`

Append to `"Agents"`:
```json
"{Name}": { "Provider": "{Provider}", "Model": "{Model}", "Temperature": 0.2, "MaxTokens": 2000 }
```

Verify `AgentsOptions` class has the matching property; add it if missing.

### 5. `tests/AgentOs.Tests/Agents/{Name}AgentTests.cs`

```csharp
using AgentOs.Domain.Llm;
using AgentOs.Modules.Pipeline.Agents;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Agents;

public class {Name}AgentTests
{
    [Fact]
    public async Task RunAsync_HappyPath_ReturnsResult() { /* mock factory + ILlmClient */ }

    [Fact]
    public async Task RunAsync_NullInput_Throws() { /* ArgumentNullException */ }

    [Fact]
    public async Task RunAsync_LlmMalformed_ThrowsLlmException() { /* malformed JSON path */ }
}
```

### 6. (Optional) Endpoint in `src/AgentOs.Modules.Pipeline/Endpoints/PipelineEndpoints.cs`

```csharp
app.MapPost("/{name}", async (I{Name}Agent agent, {Name}Input input, CancellationToken ct)
    => Results.Ok(await agent.RunAsync(input, ct)))
   .RequireAuthorization();
```

## Verify

```bash
dotnet build AgentOs.slnx
dotnet test --filter "FullyQualifiedName~{Name}AgentTests"
```

## Rules

- Never call `Anthropic.SDK` / `Azure.AI.OpenAI` directly from agents — only `ILlmClient`.
- Never hardcode model / temperature / max-tokens — read from `AgentsOptions`.
- Always return cost + token count in `Result` so QA + cost-report aggregate.
- Always log the structured `{Agent} done: …` line for cost aggregation.

## Out of scope

- Designing the system prompt (user owns).
- Designing the output schema (caller owns).
- Wiring orchestrator chain (separate concern in `PipelineOrchestrator`).
