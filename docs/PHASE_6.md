# Phase 6 — Azure deployment (Container Apps + App Insights)

> Status: ✅ Done (IaC scaffold) — 2026-05-18
> Live deployment: not performed (requires an Azure subscription + LLM credit).

## Objectives

Provide a complete IaC + CI/CD pipeline to demo `AgenticSdlc.Api` on Azure Container Apps. Supports scale-to-zero (cost ~$5-10/month for prototype dev), App Insights APM, and Key Vault for LLM secrets.

## Deliverables

### Container image

- `Dockerfile` — multi-stage build, runtime `mcr.microsoft.com/dotnet/aspnet:10.0`, non-root user uid 1654, healthcheck `/health`.
- `.dockerignore` — excludes `bin/`, `obj/`, `tests/fixtures/`, `.claude/`, secret files.

### IaC (Bicep)

- `infra/main.bicep` — single-file template, resources:
  - User-Assigned Managed Identity (for ACR pull + Key Vault Secrets read)
  - Log Analytics + Application Insights (workspace-based)
  - Azure Container Registry (Basic SKU)
  - Key Vault (RBAC, 7-day soft-delete)
  - Container Apps Environment (Consumption profile)
  - Container App with an HTTP concurrency 50 scale rule, configurable min/max replicas
- `infra/main.parameters.json` — defaults for the `dev` env
- `infra/README.md` — manual deploy guide + cost estimate

### CI/CD

- `.github/workflows/deploy.yml` — triggers on push to `main` (skips docs-only), builds + pushes to ACR + re-applies Bicep with tag = `github.sha`. OIDC federated credential (no client secret needed).

### Docs

- `docs/DEPLOYMENT.md` — one-time setup (SP + OIDC + first-time infra), subsequent deploy flow, smoke test, rollback, monitor, troubleshooting.

## Technical decisions

- **Why Container Apps and not App Service / AKS?**
  - App Service: no scale-to-zero, an idle dev instance still costs ~$50/month.
  - AKS: overkill for a single container, large ops overhead.
  - Container Apps Consumption: scale-to-zero, idle = $0; automatic HTTPS; managed identity; KEDA-based scaling. Matches the prototype scale.

- **Why a single-file Bicep, not Bicep modules?**
  - A single file ≤ 250 lines is easy to review. Modules are premature abstraction for a single deploy target.

- **Why Key Vault rather than env vars directly?**
  - The LLM API key is a long-lived secret. Rotation via Key Vault is easier. The Container App can read the secret at runtime without restarting (a revision with secretRef).

- **Why OIDC rather than a client secret?**
  - GitHub federated credentials never expire and need no rotation. The subject filter restricts the workflow to run only on `refs/heads/main` → reducing blast radius.

- **Why no actual deployment yet?**
  - There is no billing-attached Azure subscription for the thesis. The thesis will present the architecture + projected cost (Section 4.4) instead of a live demo. When a demo is needed: deploying takes ~15 minutes per `docs/DEPLOYMENT.md`.

## Projected cost (southeastasia, Q2/2026)

| Item | Idle | 1 request/s sustained |
|---|---|---|
| Container Apps (Consumption) | $0 | ~$15/month |
| Log Analytics + App Insights | $0 (free tier) | ~$3 |
| ACR Basic | ~$5 | ~$5 |
| Key Vault Standard | ~$0 | ~$0 |
| **Total infra** | **~$5/month** | **~$23/month** |
| LLM cost (Hybrid Claude+Azure) | — | ~$10-50 / 1k pipeline calls |

## Thesis references

- Section 4.1 — Prototype deployment on Azure
- Section 4.4 — Operating cost analysis
- Section 5.2 — Expansion roadmap (multi-region, blue/green)

## Next phase

Phase 6 completes the thesis scope. Expansion directions after the defense:
- **Phase 7**: Persistence (Cosmos DB) for PipelineResult history.
- **Phase 8**: Human-in-the-loop checkpoint via SignalR (`PipelineOptions.EnableHumanInTheLoop` already exists).
- **Phase 9**: Multi-region failover + blue/green deployment.
