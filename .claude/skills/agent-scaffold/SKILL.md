---
name: agent-scaffold
description: >
  Scaffold a new agent (Requirement / Coding / Testing / QA / Orchestrator hoặc custom)
  trong agentic-sdlc-net theo Clean Architecture đã thiết lập. Sinh interface trong
  Application layer, impl trong Infrastructure, đăng ký DI, stub test xUnit + fixture
  Mock client. Use when user says "scaffold agent X", "add agent X", "new agent",
  "tạo agent mới", "/agent-scaffold X". Auto-trigger khi user bắt đầu Phase 3+.
---

Scaffold 1 agent đầy đủ theo pattern Clean Arch + LLM Gateway của repo này.

## Khi nào dùng

User nói "tạo agent X" / "add agent X" / "scaffold X agent". Phase 3 (5 agent interface) + Phase 4 (orchestrator) là chính. Custom agent (vd `SecurityAgent`, `DocAgent`) cũng OK miễn fit pattern.

## Input cần hỏi (nếu thiếu)

1. **Tên agent** (PascalCase, không suffix "Agent" — sẽ tự thêm). Vd `Requirement`, `Coding`.
2. **Provider mặc định**: `Anthropic` | `AzureOpenAI` | `Mock`.
3. **Model**: vd `claude-sonnet-4-20250514`, `gpt-4.1`.
4. **Input DTO + Output DTO** shape: free-form text hoặc JSON schema → quyết định method signature.
5. **System prompt** (Vietnamese OK). Nếu user không cho, sinh stub `"TODO: system prompt cho {Agent}"` để user fill sau.

Default nếu user nói "scaffold giống Requirement Agent": dùng setting trong `appsettings.json` section `Agents:{Name}` đã có.

## Output (5 file + 2 edit)

### 1. `src/AgenticSdlc.Application/Agents/I{Name}Agent.cs`

```csharp
namespace AgenticSdlc.Application.Agents;

public interface I{Name}Agent
{
    Task<{Name}Result> RunAsync({Name}Input input, CancellationToken ct = default);
}

public sealed record {Name}Input(/* fields theo user */);
public sealed record {Name}Result(/* fields theo user */, decimal CostUsd, int InputTokens, int OutputTokens);
```

### 2. `src/AgenticSdlc.Infrastructure/Agents/{Name}Agent.cs`

```csharp
using AgenticSdlc.Application.Agents;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Infrastructure.Agents;

public sealed class {Name}Agent : I{Name}Agent
{
    private const string SystemPrompt = """
        {SystemPrompt from user, or TODO stub}
        """;

    private readonly ILlmClient _llm;
    private readonly AgentOptions _options;
    private readonly ILogger<{Name}Agent> _logger;

    public {Name}Agent(ILlmClientFactory factory, IOptionsMonitor<AgentsOptions> options, ILogger<{Name}Agent> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        _options = options.CurrentValue.{Name} ?? throw new InvalidOperationException("Agents:{Name} not configured.");
        _llm = factory.Create(_options.Provider);
        _logger = logger;
    }

    public async Task<{Name}Result> RunAsync({Name}Input input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = new LlmRequest(
            SystemPrompt: SystemPrompt,
            UserPrompt: BuildUserPrompt(input),
            Model: _options.Model,
            Temperature: _options.Temperature,
            MaxTokens: _options.MaxTokens);

        var response = await _llm.SendAsync(request, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "{{Agent}} done: {{InTok}}→{{OutTok}} tokens, ${{Cost}}, {{Ms}}ms",
            nameof({Name}Agent), response.InputTokens, response.OutputTokens, response.CostUsd, response.Latency.TotalMilliseconds);

        return Parse(response, input);
    }

    private static string BuildUserPrompt({Name}Input input) =>
        $"""
        TODO: format input thành prompt
        Input: {{input}}
        """;

    private static {Name}Result Parse(LlmResponse response, {Name}Input input)
    {
        // TODO: nếu structured output (JSON), deserialize response.Content
        // throw LlmException nếu malformed
        return new {Name}Result(
            /* fields */,
            CostUsd: response.CostUsd,
            InputTokens: response.InputTokens,
            OutputTokens: response.OutputTokens);
    }
}
```

### 3. Edit `src/AgenticSdlc.Infrastructure/DependencyInjection.cs`

Thêm `services.AddTransient<I{Name}Agent, {Name}Agent>();`. Nếu file chưa có, tạo `AgenticSdlc.Infrastructure/Agents/DependencyInjection.cs` với extension `AddAgents(this IServiceCollection)`.

### 4. Edit `src/AgenticSdlc.Api/appsettings.json`

Thêm vào section `Agents` nếu chưa có:

```json
"{Name}": { "Provider": "{Provider}", "Model": "{Model}", "Temperature": 0.2, "MaxTokens": 2000 }
```

Verify section `Agents` đã match `AgentsOptions` class (Phase 3 sẽ tạo nếu chưa có).

### 5. `tests/AgenticSdlc.Tests/Agents/{Name}AgentTests.cs`

```csharp
using AgenticSdlc.Application.Agents;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Agents;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Agents;

public class {Name}AgentTests
{
    [Fact]
    public async Task RunAsync_HappyPath_ReturnsResult()
    {
        // Arrange
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(new LlmResponse("stub", 10, 20, 0.0001m, TimeSpan.FromMilliseconds(50), "{Model}", "{Provider}"));

        var factory = Substitute.For<ILlmClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(llm);

        // var sut = new {Name}Agent(factory, /* options stub */, NullLogger);
        // ...
    }

    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        // ArgumentNullException test
    }

    [Fact]
    public async Task RunAsync_LlmMalformed_ThrowsLlmException()
    {
        // Malformed JSON path
    }
}
```

### 6. Fixture `tests/fixtures/llm/<hash>.json`

Compute hash bằng `MockLlmClient.ComputeHash(new LlmRequest(SystemPrompt, sample user prompt, Model))`. Sinh 1-2 fixture happy-path để Mock client trả deterministic. Format:

```json
{
  "content": "expected output here",
  "inputTokens": 120,
  "outputTokens": 80
}
```

### 7. (Optional) Endpoint trong `src/AgenticSdlc.Api/Program.cs`

```csharp
app.MapPost("/{name}", async (I{Name}Agent agent, {Name}Input input, CancellationToken ct)
    => Results.Ok(await agent.RunAsync(input, ct)));
```

## Verification

Sau khi scaffold xong:

```bash
dotnet build AgenticSdlc.sln          # 0 warn, 0 err (WarningsAsErrors active)
dotnet test --filter "FullyQualifiedName~{Name}AgentTests"
```

Nếu test fail vì stub Mock provider không có fixture đúng hash → log warning + bảo user invoke `/fixture-record` (skill sister) để generate fixture từ real API call.

## Pattern phải tuân

- **KHÔNG** gọi trực tiếp `Anthropic.SDK` / `Azure.AI.OpenAI` từ agent — chỉ qua `ILlmClient`.
- **KHÔNG** hardcode model / temperature / max-tokens — đọc từ `AgentsOptions`.
- **PHẢI** return cost + token count trong Result để QA Agent + cost-report skill aggregate được.
- **PHẢI** dùng `_logger.LogInformation` ghi token + cost mỗi call (Application Insights sẽ scrape).
- **Vietnamese comment OK** cho domain code (project convention).

## Out of scope

Skill này KHÔNG:
- Tự design system prompt (user/luận văn quyết định).
- Tự design output schema (cần spec từ Mục 2.4 luận văn).
- Setup pipeline orchestrator (Phase 4, dùng skill riêng).
