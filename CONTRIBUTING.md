# Contributing to agentic-sdlc-net

Thanks for your interest in contributing! This project is a .NET-native multi-agent platform
for the software development lifecycle. Issues and pull requests are welcome.

## Getting started

```bash
git clone https://github.com/hoangsnowy/agentic-sdlc-net.git
cd agentic-sdlc-net
dotnet restore AgenticSdlc.sln
dotnet build   AgenticSdlc.sln -c Release
dotnet test    AgenticSdlc.sln -c Release
```

Prerequisites: the **.NET 10 SDK** (pinned via `global.json`). No API keys are required —
the offline `MockLlmClient` path covers build, tests, and the AgentOS demo. See
[docs/SETUP.md](docs/SETUP.md) for secrets, local Postgres, and CI configuration.

## Development workflow

1. Branch from `main` (`feat/…`, `fix/…`, `chore/…`).
2. Make your change with tests. Keep `dotnet build -c Release` warning-free —
   `TreatWarningsAsErrors` and `Nullable` are enabled solution-wide.
3. `dotnet test -c Release` must pass.
4. Open a PR and fill in [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md)
   (summary + test plan). CI runs restore → build → test on every PR.

## Conventions

- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/) in English
  (`feat(llm): …`, `fix(ui): …`, `docs: …`, `chore(deps): …`).
- **Tests**: xUnit v3 + Shouldly + NSubstitute. Name `MethodName_StateUnderTest_ExpectedBehavior`.
- **Architecture**: Clean Architecture — dependencies point inward
  (`Api`/`Web` → `Infrastructure` → `Application` → `Domain`). Don't add a reference that
  reverses that direction.
- **LLM access**: agents depend on `ILlmClient`, never on a vendor SDK directly. A new provider
  is a new `ILlmClient` + a `LlmClientFactory` case.
- **Language**: code, comments, and docs are English.

## Reporting bugs / requesting features

Use the issue templates under [`.github/ISSUE_TEMPLATE`](.github/ISSUE_TEMPLATE). For anything
security-related, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
