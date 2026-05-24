# agentic-sdlc-net

> Reference prototype cho mô hình **Multi-Agent AI** hỗ trợ vòng đời phát triển phần mềm (SDLC), thực hiện trên **.NET 10** và **Microsoft Azure**, sử dụng kiến trúc **hybrid LLM** (Claude — Anthropic API + GPT — Azure OpenAI Service).

Đây là sản phẩm đi kèm đề án thạc sĩ **"Nghiên cứu và Ứng dụng Mô hình Agentic AI trong Quy trình Phát triển Phần mềm (SDLC)"** — Nguyễn Minh Hoàng, Đại học Kinh doanh và Công nghệ Hà Nội, 2026.

---

## Mục tiêu

Prototype hiện thực hoá kiến trúc **Leader-Specialists-Quality Loop** với 5 tác tử:

| Tác tử | Vai trò | LLM mặc định |
|---|---|---|
| **Orchestrator Agent** | Điều phối trung tâm, phân công, tổng hợp | Claude Haiku 4.5 |
| **Requirement Agent** | Phân tích yêu cầu → Structured Requirements JSON | Claude Sonnet 4 |
| **Coding Agent** | Sinh mã khung C# theo Clean Architecture | GPT-4.1 (Azure OpenAI) |
| **Testing Agent** | Sinh xUnit test cases (happy / edge / error) | GPT-4o-mini (Azure OpenAI) |
| **QA Agent** | Đánh giá nhất quán requirement-code-test, vòng lặp tối đa 3 vòng | Claude Haiku 4.5 |

Việc gán LLM cho từng tác tử có thể cấu hình qua `appsettings.json` — kiến trúc *Platform Agnostic*.

---

## Kiến trúc

Solution gồm 5 project, tổ chức theo Clean Architecture:

```
agentic-sdlc-net/
├── src/
│   ├── AgenticSdlc.Domain/         # Entities, value objects (RequirementSpec, CodeArtifact, ...)
│   ├── AgenticSdlc.Application/    # Interfaces tác tử (IRequirementAgent, ...)
│   ├── AgenticSdlc.Infrastructure/ # LLM Gateway (ClaudeClient, AzureOpenAiClient), agent impls
│   └── AgenticSdlc.Api/            # ASP.NET Core minimal API host
└── tests/
    └── AgenticSdlc.Tests/          # xUnit unit + integration tests
```

LLM Gateway expose interface `ILlmClient` với 2 implementation song song (`ClaudeClient`, `AzureOpenAiClient`) — đăng ký qua DI. Mỗi tác tử nhận `ILlmClient` (đã được factory chọn đúng cho vai trò) thay vì gọi trực tiếp SDK của hãng.

---

## Yêu cầu môi trường

- **.NET 10 SDK** (LTS, released 11/2025).
- Một trong hai (hoặc cả hai) tài khoản LLM:
  - **Anthropic API key** — tạo tại <https://console.anthropic.com>
  - **Azure OpenAI Service** — tạo deployment cho `gpt-4.1` và `gpt-4o-mini` qua Azure Portal
- (Tuỳ chọn) **Azure Cosmos DB** cho persistence; mặc định prototype dùng in-memory store.

Verify .NET 10:

```bash
dotnet --list-sdks
# Phải có dòng bắt đầu bằng "10."
```

---

## Cấu hình

Sao chép `src/AgenticSdlc.Api/appsettings.json` thành `appsettings.Development.json` (đã có trong `.gitignore`) và điền secret:

```json
{
  "Llm": {
    "Anthropic": {
      "ApiKey":   "sk-ant-...",
      "BaseUrl":  "https://api.anthropic.com",
      "Version":  "2023-06-01"
    },
    "AzureOpenAI": {
      "Endpoint": "https://<your-resource>.openai.azure.com",
      "ApiKey":   "<your-key>"
    }
  },
  "Agents": {
    "Orchestrator": { "Provider": "Anthropic",   "Model": "claude-haiku-4-5",   "Temperature": 0.3, "MaxTokens": 2000 },
    "Requirement":  { "Provider": "Anthropic",   "Model": "claude-sonnet-4",    "Temperature": 0.1, "MaxTokens": 2000 },
    "Coding":       { "Provider": "AzureOpenAI", "Model": "gpt-4.1",            "Temperature": 0.2, "MaxTokens": 4000 },
    "Testing":      { "Provider": "AzureOpenAI", "Model": "gpt-4o-mini",        "Temperature": 0.2, "MaxTokens": 3000 },
    "Qa":           { "Provider": "Anthropic",   "Model": "claude-haiku-4-5",   "Temperature": 0.1, "MaxTokens": 1500 }
  }
}
```

Trong môi trường production hoặc CI, dùng **Azure Key Vault** hoặc **GitHub Actions Secrets** thay vì file.

---

## Build & Run

```bash
git clone https://github.com/<your-org-or-user>/agentic-sdlc-net.git
cd agentic-sdlc-net

# Restore + build
dotnet restore
dotnet build

# Chạy unit tests
dotnet test

# Chạy API local
dotnet run --project src/AgenticSdlc.Api
# Scalar UI:  http://localhost:5080/scalar/v1
# OpenAPI:    http://localhost:5080/openapi/v1.json
```

### Demo end-to-end

```bash
curl -X POST http://localhost:5080/pipeline \
  -H "Content-Type: application/json" \
  -d '{"userStory":"Hệ thống cần API quản lý sản phẩm cho phép admin tạo/xem/sửa/xoá; người dùng tra cứu theo danh mục.","nMax":3}'
```

---

## API Endpoints

| Method | Path | Mô tả |
|---|---|---|
| `POST` | `/requirement` | Gọi riêng Requirement Agent |
| `POST` | `/code` | Gọi riêng Coding Agent |
| `POST` | `/test` | Gọi riêng Testing Agent |
| `POST` | `/qa` | Gọi riêng QA Agent |
| `POST` | `/pipeline` | Chạy luồng end-to-end (KC4 trong đề án) |
| `GET`  | `/health` | Healthcheck |

---

## Lộ trình

- [x] Phase 1 — Solution skeleton, CI, README
- [x] Phase 2 — LLM Gateway (`ILlmClient` + 2 impls + factory + Mock)
- [x] Phase 3 — Domain models + 5 agent interfaces
- [x] Phase 4 — `PipelineOrchestrator` + endpoints
- [x] Phase 5 — Unit tests + benchmark KC1–KC5
- [x] Phase 6 — Azure deployment (Container Apps + App Insights)
- [x] Phase 7 — Agent Studio (Blazor Server, realtime UI + orchestration editor) — xem [docs/PHASE_7.md](docs/PHASE_7.md)

---

## Tham chiếu đề án

- Mục 2.2 — Kiến trúc Multi-Agent đề xuất
- Mục 2.4 — Triển khai prototype
- Mục 2.5 — Kịch bản thực nghiệm KC1–KC5

---

## License

MIT — xem [LICENSE](./LICENSE).

---

## English summary

Reference prototype for a **multi-agent AI system** that supports the software development lifecycle (SDLC), built on **.NET 10** and **Microsoft Azure**, using a **hybrid LLM** strategy (Anthropic Claude + Azure OpenAI). The system orchestrates five specialised agents — Orchestrator, Requirement, Coding, Testing, QA — through a leader-specialists pattern with an explicit Quality Loop (max 3 iterations). Companion to the Master's thesis *"Research and Application of Agentic AI in the Software Development Lifecycle"* (Nguyen Minh Hoang, HUBT, 2026).
