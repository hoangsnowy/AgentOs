---
name: fixture-record
description: >
  Record real LLM response (Claude hoặc Azure OpenAI) thành fixture JSON cho
  MockLlmClient của agentic-sdlc-net. Compute SHA-256 hash từ (model + system + user prompt),
  save vào tests/fixtures/llm/<hash>.json. Cho test offline + CI không API key. Use when user
  says "record fixture for X", "save mock response", "fixture cho prompt này", "/fixture-record".
  Auto-trigger khi viết test mới cho agent dùng MockLlmClient mà chưa có fixture matching.
---

Record 1 response thật từ Claude / Azure OpenAI thành fixture JSON cho `MockLlmClient`.

## Khi nào dùng

- User viết test mới gọi `MockLlmClient`, test fail vì hash miss → cần fixture.
- User chuẩn bị demo deterministic (KC4 pipeline) → freeze response.
- User muốn snapshot output 1 prompt để compare drift sau khi đổi model.

## Input cần thu thập

1. **System prompt** (string, có thể rỗng).
2. **User prompt** (string, bắt buộc).
3. **Model** (vd `claude-sonnet-4-20250514`, `gpt-4.1`).
4. **Provider**: `Anthropic` | `AzureOpenAI`.
5. **Temperature, MaxTokens** (optional, default 0.0 / 4096).

Nếu user reference 1 file test cụ thể, đọc test → extract prompt trực tiếp.

## Steps

### 1. Compute hash trước

Hash logic giống `MockLlmClient.ComputeHash` (file `src/AgenticSdlc.Infrastructure/Llm/MockLlmClient.cs`):

- Input string: `$"{Model}\n---\n{SystemPrompt}\n---\n{UserPrompt}"`
- SHA-256 → lấy 8 byte đầu → hex lowercase (16 chars).

Implement bằng dotnet CLI 1-liner để verify:

```bash
dotnet script -e '
using System.Security.Cryptography; using System.Text;
var s = "{Model}\n---\n{System}\n---\n{User}";
var h = SHA256.HashData(Encoding.UTF8.GetBytes(s));
Console.WriteLine(Convert.ToHexString(h, 0, 8).ToLowerInvariant());
'
```

Hoặc dùng `MockLlmClient.ComputeHash` qua test runner. Cheapest: viết test 1-shot in `tests/AgenticSdlc.Tests/Llm/FixtureHashScratch.cs`, chạy, đọc output, xoá file.

### 2. Verify fixture đã có chưa

```bash
ls tests/fixtures/llm/<hash>.json 2>&1
```

Nếu có rồi: hỏi user overwrite không. Mặc định KHÔNG overwrite (fixture được dùng làm baseline; overwrite ngầm gây drift test).

### 3. Gọi real API

Phải có `user-secrets` setup. Verify:

```bash
cd src/AgenticSdlc.Api
dotnet user-secrets list | grep -i apikey
```

Nếu chưa có → bảo user setup theo `docs/SETUP.md` mục 3.

Viết 1 test script tạm `tests/AgenticSdlc.Tests/Llm/Scratch_RecordFixture.cs`:

```csharp
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

[Trait("Category", "Live")]
public class Scratch_RecordFixture
{
    [Fact(Skip = "Manual: chạy với --filter \"FullyQualifiedName~Scratch_RecordFixture\" để record fixture")]
    public async Task Record()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Scratch_RecordFixture>()
            .Build();

        var services = new ServiceCollection()
            .AddLogging(b => b.AddConsole())
            .AddLlmGateway(config)
            .BuildServiceProvider();

        var factory = services.GetRequiredService<ILlmClientFactory>();
        var client = factory.Create("{Provider}");

        var request = new LlmRequest(
            SystemPrompt: "{System}",
            UserPrompt: "{User}",
            Model: "{Model}",
            Temperature: {Temp},
            MaxTokens: {Max});

        var response = await client.SendAsync(request);

        var fixture = new
        {
            content = response.Content,
            inputTokens = response.InputTokens,
            outputTokens = response.OutputTokens,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(fixture, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var hash = MockLlmClient.ComputeHash(request);
        var path = System.IO.Path.Combine("tests", "fixtures", "llm", $"{hash}.json");
        await System.IO.File.WriteAllTextAsync(path, json);
        Console.WriteLine($"Wrote fixture: {path}");
    }
}
```

Chạy có target:

```bash
dotnet test --filter "FullyQualifiedName~Scratch_RecordFixture" -- xunit.runner.json
# hoặc: tạm bỏ Skip, dotnet test --filter "...", khôi phục Skip sau khi xong.
```

### 4. Verify fixture đã write

```bash
cat tests/fixtures/llm/<hash>.json
```

Shape phải:
```json
{
  "content": "...",
  "inputTokens": 123,
  "outputTokens": 456
}
```

(camelCase, `MockLlmClient` dùng `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`).

### 5. Cleanup scratch file

Xoá `Scratch_RecordFixture.cs` sau khi xong (file này gọi real API + tốn token, không nên commit).

### 6. Commit fixture

```bash
git add tests/fixtures/llm/<hash>.json
git commit -m "test(fixtures): record <agent-name> <scenario> fixture (<model>)"
```

## Safety

- **KHÔNG** commit `Scratch_RecordFixture.cs` (gọi real API → CI sẽ fail vì không có key).
- **KHÔNG** record fixture cho prompt có PII / secret user nhập (vd email, internal endpoint).
- **CẢNH BÁO** user nếu prompt + model sẽ tốn > $0.05 (estimate qua `CostCalculator` trước khi gọi).
- **Fixture có version**: mỗi lần đổi model alias (vd `claude-sonnet-4` → `claude-sonnet-4-5`), hash thay đổi → fixture cũ stale; phải re-record hoặc giữ cả 2.

## Out of scope

- Replay fixture (đã có `MockLlmClient` tự handle).
- Batch record nhiều prompt (skill `kc-bench` lo việc đó cho benchmark).
- So sánh fixture cũ vs mới (skill `prompt-tune` lo việc đó).
