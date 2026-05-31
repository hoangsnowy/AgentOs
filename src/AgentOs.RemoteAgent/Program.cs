// AgentOs.RemoteAgent — the dev-side agent for the "remote dev-IDE agent" runtime.
// Connects to the API's SignalR hub, receives requests, and executes them locally.
//
// M3 — "Execute": receives a full LLM prompt, runs it through a local CLI (e.g. claude -p),
//       returns the text result.
// M4 — "ExecuteToolCall": receives a single tool-execution request from the server's agentic
//       loop, executes the shell command locally, returns stdout/stderr. The LLM reasoning stays
//       on the server; the runner only provides the execution environment.
//
// Configure via environment variables:
//   REMOTE_AGENT_HUB    hub URL      (default https://localhost:5080/hubs/remote-agent)
//   REMOTE_AGENT_ID     runner id    (the Guid returned by POST /runners)
//   REMOTE_AGENT_TOKEN  pairing token (the plaintext returned ONCE by POST /runners)
//   REMOTE_AGENT_CMD    command to run for M3 Execute (default "claude")
//   REMOTE_AGENT_ARGS   command args  (default "-p")  — the prompt is piped to stdin

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

const int MaxOutputBytes = 32 * 1024; // 32 KB cap on tool output

var hubUrl = Environment.GetEnvironmentVariable("REMOTE_AGENT_HUB") ?? "https://localhost:5080/hubs/remote-agent";
var runnerId = Environment.GetEnvironmentVariable("REMOTE_AGENT_ID") ?? "";
var token = Environment.GetEnvironmentVariable("REMOTE_AGENT_TOKEN") ?? "";

var connection = new HubConnectionBuilder()
    .WithUrl($"{hubUrl}?runnerId={Uri.EscapeDataString(runnerId)}&token={Uri.EscapeDataString(token)}")
    .WithAutomaticReconnect()
    .Build();

// M3 — full-prompt LLM dispatch
connection.On<RemoteExecRequest>("Execute", async request =>
{
    Console.WriteLine($"[agent] Execute {request.Id} (model={request.Model})");
    var result = await RunLlmAsync(request).ConfigureAwait(false);
    await connection.SendAsync("CompleteRequest", result).ConfigureAwait(false);
    Console.WriteLine($"[agent] -> {request.Id} ok={result.Ok}");
});

// M4 — server-driven tool execution
connection.On<RunnerToolCall>("ExecuteToolCall", async call =>
{
    Console.WriteLine($"[agent] ExecuteToolCall {call.ToolCallId} ({call.ToolName})");
    var result = await ExecuteToolAsync(call).ConfigureAwait(false);
    await connection.SendAsync("CompleteToolCall", result).ConfigureAwait(false);
    Console.WriteLine($"[agent] -> {call.ToolCallId} ok={result.Ok}");
});

await connection.StartAsync().ConfigureAwait(false);
Console.WriteLine($"[agent] connected to {hubUrl}; runner id={runnerId}. Ctrl+C to exit.");
await Task.Delay(Timeout.Infinite).ConfigureAwait(false);

// ── M3: run the LLM CLI with the prompt piped to stdin ──────────────────────────────────────────

static async Task<RemoteExecResult> RunLlmAsync(RemoteExecRequest request)
{
    var cmd = Environment.GetEnvironmentVariable("REMOTE_AGENT_CMD") ?? "claude";
    var cmdArgs = Environment.GetEnvironmentVariable("REMOTE_AGENT_ARGS") ?? "-p";
    try
    {
        var psi = new ProcessStartInfo(cmd)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in cmdArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{cmd}'.");

        var prompt = string.IsNullOrEmpty(request.SystemPrompt)
            ? request.UserPrompt
            : request.SystemPrompt + "\n\n" + request.UserPrompt;
        await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        return process.ExitCode == 0
            ? new RemoteExecResult(request.Id, true, stdout.Trim(), null)
            : new RemoteExecResult(request.Id, false, string.Empty, $"exit {process.ExitCode}: {stderr.Trim()}");
    }
    catch (Exception ex)
    {
        return new RemoteExecResult(request.Id, false, string.Empty, ex.Message);
    }
}

// ── M4: execute a single tool call (shell command) ──────────────────────────────────────────────

static async Task<RunnerToolResult> ExecuteToolAsync(RunnerToolCall call)
{
    try
    {
        if (!string.Equals(call.ToolName, "shell", StringComparison.OrdinalIgnoreCase))
        {
            return new RunnerToolResult(call.RequestId, call.ToolCallId, false, string.Empty,
                $"Unknown tool '{call.ToolName}'. Supported: shell.");
        }

        using var doc = JsonDocument.Parse(call.JsonInput);
        var root = doc.RootElement;

        var command = root.GetProperty("command").GetString()
            ?? throw new InvalidOperationException("Missing 'command' in tool input.");

        string? workingDir = null;
        if (root.TryGetProperty("working_dir", out var wdProp))
        {
            workingDir = wdProp.GetString();
        }

        // Use the OS shell to handle quoting, pipes, and redirections correctly.
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "cmd.exe" : "sh";
        var shellFlag = isWindows ? "/c" : "-c";

        var psi = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(shellFlag);
        psi.ArgumentList.Add(command);
        if (!string.IsNullOrEmpty(workingDir))
        {
            psi.WorkingDirectory = workingDir;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start shell process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        // Cap output to avoid flooding the server with huge build logs.
        var combined = string.IsNullOrEmpty(stderr)
            ? stdout
            : stdout + "\n[stderr]\n" + stderr;

        if (combined.Length > MaxOutputBytes)
        {
            combined = combined[..MaxOutputBytes] + $"\n[output truncated at {MaxOutputBytes / 1024} KB]";
        }

        var ok = process.ExitCode == 0;
        return new RunnerToolResult(
            call.RequestId,
            call.ToolCallId,
            ok,
            combined.Trim(),
            ok ? null : $"exit {process.ExitCode}");
    }
    catch (Exception ex)
    {
        return new RunnerToolResult(call.RequestId, call.ToolCallId, false, string.Empty, ex.Message);
    }
}

// ── DTOs — must match the server's shapes ───────────────────────────────────────────────────────

internal sealed record RemoteExecRequest(string Id, string SystemPrompt, string UserPrompt, string Model);
internal sealed record RemoteExecResult(string Id, bool Ok, string Content, string? Error);
internal sealed record RunnerToolCall(string RequestId, string ToolCallId, string ToolName, string JsonInput);
internal sealed record RunnerToolResult(string RequestId, string ToolCallId, bool Ok, string JsonOutput, string? Error);
