# AgentOS Runner (VS Code)

Pair this machine to an AgentOS server and run AI coding sessions locally — a one-click sign-in instead of copying tokens into a terminal.

## Use

1. Run **AgentOS: Connect this machine** from the Command Palette.
2. Approve the pairing in the browser (log in if prompted).
3. The status bar shows **AgentOS: Connected** — this machine is now a runner; AgentOS dispatches sessions to it.

The extension downloads the AgentOS runner binary from your server and runs it for you — nothing to install by hand, no env vars, no token copy-paste.

## Settings

- `agentos.serverUrl` — AgentOS server base URL (default `https://localhost:5180`).
- `agentos.runOnMyMachine` — run sessions with the local `claude` CLI (adds `--dangerously-skip-permissions`; requires the `claude` CLI installed and logged in).

## Commands

- **AgentOS: Connect this machine** — pair and start the runner.
- **AgentOS: Disconnect** — stop the runner and clear the stored token (revoke server-side in the AgentOS Runners tab).
- **AgentOS: Show runner logs** — open the runner output channel.

## Notes

- Ships the Windows (win-x64) runner binary for now; macOS/Linux are planned.
- On `localhost` the server's development TLS certificate is accepted automatically.

## Develop

```bash
npm install
npm run build          # esbuild -> dist/extension.js
npm run package        # -> agentos-runner.vsix
code --install-extension agentos-runner.vsix
```
