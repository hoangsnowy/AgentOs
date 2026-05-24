# Setup & First Run

A step-by-step guide to build, run, and push the `agentic-sdlc-net` repo to GitHub.

## 1. Install the .NET 10 SDK

Download from <https://dotnet.microsoft.com/download/dotnet/10.0>, choosing the x64 SDK (Windows / macOS / Linux).

Verify:

```bash
dotnet --list-sdks
# 10.0.100 [C:\Program Files\dotnet\sdk]
```

If the output has no line starting with `10.`, check `global.json` at the repo root (it pins `10.0.100`).

## 2. First build & test

From the `D:\LuanVan\prototype\` folder:

```bash
dotnet restore AgenticSdlc.sln
dotnet build  AgenticSdlc.sln --configuration Release
dotnet test   AgenticSdlc.sln --configuration Release
```

Phase 1 has only 1 smoke test; the expected result is `Passed: 1`.

## 3. Configure LLM secrets (local)

Use .NET User Secrets so keys are never committed:

```bash
cd src/AgenticSdlc.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:Anthropic:ApiKey"  "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAI:ApiKey" "..."
dotnet user-secrets set "Llm:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com"
```

Secrets are stored in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, **not in the repo**.

## 4. Run the API locally

```bash
cd src/AgenticSdlc.Api
dotnet run
```

Browse to:

- Health: <http://localhost:5080/health>
- Scalar API Reference (UI): <http://localhost:5080/scalar/v1>
- OpenAPI spec (JSON): <http://localhost:5080/openapi/v1.json>

## 5. Push to GitHub

The first time (in the `D:\LuanVan\prototype\` folder):

```bash
git init
git add .
git commit -m "chore: phase 1 — initial scaffold (.NET 10 solution + CI)"
git branch -M main

# Create the repo on GitHub (via the web or the gh CLI):
#   gh repo create agentic-sdlc-net --public --description "Multi-agent AI for SDLC — companion to Master's thesis"
git remote add origin https://github.com/<your-username>/agentic-sdlc-net.git
git push -u origin main
```

Check the **Actions** tab on GitHub — the CI workflow `.github/workflows/ci.yml` will run automatically the first time.

## 6. Configure GitHub Actions secrets (for CI to call the LLM)

In GitHub: **Settings → Secrets and variables → Actions → New repository secret**

| Name | Value |
|---|---|
| `ANTHROPIC_API_KEY` | sk-ant-... |
| `AZURE_OPENAI_ENDPOINT` | https://\<resource\>.openai.azure.com |
| `AZURE_OPENAI_API_KEY` | ... |

Phase 5 (experimental tests with a real LLM) will add a workflow that reads these secrets.

## 7. Branch protection (recommended)

**Settings → Branches → Add rule:** `main`

- ☑ Require a pull request before merging
- ☑ Require status checks to pass before merging — select `Build & Test`
- ☑ Require linear history (rebase / squash merge only)

---

**Next phase:** Phase 2 — LLM Gateway. Will add `ILlmClient`, `ClaudeClient`, `AzureOpenAiClient` and DI registration. The detailed guide will be updated in `docs/PHASE_2.md`.
