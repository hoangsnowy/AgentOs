// AgentOs.RemoteAgent — the dev-side agent for the "remote dev-IDE agent" runtime.
// Connects to the API's SignalR hub, receives codegen requests, runs them through a local CLI
// (e.g. the Claude Code CLI) on the dev's own quota — so the server spends no API tokens — and
// streams the result back. Configure via environment variables:
//   REMOTE_AGENT_HUB    hub URL      (default https://localhost:5080/hubs/remote-agent)
//   REMOTE_AGENT_ID     runner id    (the Guid returned by POST /runners)
//   REMOTE_AGENT_TOKEN  pairing token (the plaintext returned ONCE by POST /runners)
//   REMOTE_AGENT_CMD    command to run (default "claude")
//   REMOTE_AGENT_ARGS   command args  (default "-p")  — the prompt is piped to stdin

using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;

var hubUrl = Environment.GetEnvironmentVariable("REMOTE_AGENT_HUB") ?? "https://localhost:5080/hubs/remote-agent";
var runnerId = Environment.GetEnvironmentVariable("REMOTE_AGENT_ID") ?? "";
var token = Environment.GetEnvironmentVariable("REMOTE_AGENT_TOKEN") ?? "";
var command = Environment.GetEnvironmentVariable("REMOTE_AGENT_CMD") ?? "claude";
var commandArgs = Environment.GetEnvironmentVariable("REMOTE_AGENT_ARGS") ?? "-p";

var connection = new HubConnectionBuilder()
    .WithUrl($"{hubUrl}?runnerId={Uri.EscapeDataString(runnerId)}&token={Uri.EscapeDataString(token)}")
    .WithAutomaticReconnect()
    .Build();

connection.On<RemoteExecRequest>("Execute", async request =>
{
    Console.WriteLine($"[agent] Execute {request.Id} (model={request.Model})");
    var result = await RunAsync(request).ConfigureAwait(false);
    await connection.SendAsync("CompleteRequest", result).ConfigureAwait(false);
    Console.WriteLine($"[agent] -> {request.Id} ok={result.Ok}");
});

await connection.StartAsync().ConfigureAwait(false);
Console.WriteLine($"[agent] connected to {hubUrl}; runner = '{command} {commandArgs}'. Ctrl+C to exit.");
await Task.Delay(Timeout.Infinite).ConfigureAwait(false);

static async Task<RemoteExecResult> RunAsync(RemoteExecRequest request)
{
    var command = Environment.GetEnvironmentVariable("REMOTE_AGENT_CMD") ?? "claude";
    var commandArgs = Environment.GetEnvironmentVariable("REMOTE_AGENT_ARGS") ?? "-p";
    try
    {
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in commandArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{command}'.");

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

// Wire DTOs — must match the server's RemoteExecRequest/RemoteExecResult shape.
internal sealed record RemoteExecRequest(string Id, string SystemPrompt, string UserPrompt, string Model);
internal sealed record RemoteExecResult(string Id, bool Ok, string Content, string? Error);
