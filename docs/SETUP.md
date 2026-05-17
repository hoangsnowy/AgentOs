# Setup & First Run

Hướng dẫn từng bước để build, chạy và push repo `agentic-sdlc-net` lên GitHub.

## 1. Cài .NET 10 SDK

Tải từ <https://dotnet.microsoft.com/download/dotnet/10.0>, chọn SDK x64 (Windows / macOS / Linux).

Verify:

```bash
dotnet --list-sdks
# 10.0.100 [C:\Program Files\dotnet\sdk]
```

Nếu output không có dòng bắt đầu bằng `10.`, kiểm tra `global.json` ở root repo (đã pin `10.0.100`).

## 2. Build & test lần đầu

Từ folder `D:\LuanVan\prototype\`:

```bash
dotnet restore AgenticSdlc.sln
dotnet build  AgenticSdlc.sln --configuration Release
dotnet test   AgenticSdlc.sln --configuration Release
```

Phase 1 chỉ có 1 smoke test, kết quả mong đợi `Passed: 1`.

## 3. Cấu hình LLM secret (local)

Dùng .NET User Secrets để không commit key:

```bash
cd src/AgenticSdlc.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:Anthropic:ApiKey"  "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAI:ApiKey" "..."
dotnet user-secrets set "Llm:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com"
```

Secret lưu trong `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, **không nằm trong repo**.

## 4. Chạy API local

```bash
cd src/AgenticSdlc.Api
dotnet run
```

Truy cập:

- Health: <http://localhost:5080/health>
- Scalar API Reference (UI): <http://localhost:5080/scalar/v1>
- OpenAPI spec (JSON): <http://localhost:5080/openapi/v1.json>

## 5. Push lên GitHub

Lần đầu tiên (trong folder `D:\LuanVan\prototype\`):

```bash
git init
git add .
git commit -m "chore: phase 1 — initial scaffold (.NET 10 solution + CI)"
git branch -M main

# Tạo repo trên GitHub (qua web hoặc gh CLI):
#   gh repo create agentic-sdlc-net --public --description "Multi-agent AI for SDLC — companion to Master's thesis"
git remote add origin https://github.com/<your-username>/agentic-sdlc-net.git
git push -u origin main
```

Kiểm tra tab **Actions** trên GitHub — CI workflow `.github/workflows/ci.yml` sẽ tự chạy lần đầu.

## 6. Cấu hình GitHub Actions secret (cho CI gọi LLM)

Trong GitHub: **Settings → Secrets and variables → Actions → New repository secret**

| Tên | Giá trị |
|---|---|
| `ANTHROPIC_API_KEY` | sk-ant-... |
| `AZURE_OPENAI_ENDPOINT` | https://\<resource\>.openai.azure.com |
| `AZURE_OPENAI_API_KEY` | ... |

Phase 5 (test thực nghiệm có LLM thật) sẽ thêm workflow đọc secret này.

## 7. Branch protection (khuyến nghị)

**Settings → Branches → Add rule:** `main`

- ☑ Require a pull request before merging
- ☑ Require status checks to pass before merging — chọn `Build & Test`
- ☑ Require linear history (chỉ rebase / squash merge)

---

**Phase tiếp theo:** Phase 2 — LLM Gateway. Sẽ thêm `ILlmClient`, `ClaudeClient`, `AzureOpenAiClient` và DI registration. Phần hướng dẫn cụ thể sẽ được cập nhật ở `docs/PHASE_2.md`.
