# Security Policy

## Reporting a vulnerability

Please do **not** open a public issue for security vulnerabilities. Instead, report privately via
[GitHub Security Advisories](https://github.com/hoangsnowy/AgentOs/security/advisories/new)
or email the maintainer. We aim to acknowledge reports within a few days.

When reporting, include: affected component, reproduction steps, and impact.

## Scope notes

- **LLM keys & secrets** are never committed. Set them via `dotnet user-secrets`, environment
  variables, or the runtime Settings store (encrypted with ASP.NET Core Data Protection). See
  [docs/SETUP.md](docs/SETUP.md).
- **API auth** is JWT bearer; protect the signing secret and rotate it via the settings store.
- The **remote dev-agent runtime** executes work on a developer machine — treat the pairing
  credential as a secret and run it behind an approval gate (tracked in the runtime design).

## Supported versions

This project is pre-1.0; security fixes land on `main`.
