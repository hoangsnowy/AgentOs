// Epic E3 — Manages the live MCP client connections. On startup, walks the configured server list,
// spins up a transport per server (stdio subprocess or HTTP/SSE), creates an McpClient, lists the
// remote tools, wraps each into McpToolAdapter and registers it in IToolRegistry under the
// prefixed name "{server}.{tool}". Failure to connect to one server is logged and skipped — other
// servers and the rest of the host still come up. Connections are kept open for the lifetime of
// the process; IDisposable in the host disposes them all on shutdown.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AgentOs.Modules.Mcp;

/// <summary>Connects to configured MCP servers and registers their tools.</summary>
public sealed class McpClientHost : IAsyncDisposable
{
    private readonly McpOptions _options;
    private readonly IToolRegistry _registry;
    private readonly ILogger<McpClientHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<McpClient> _clients = new();
    private readonly List<string> _registeredNames = new();

    public McpClientHost(
        IOptions<McpOptions> options,
        IToolRegistry registry,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new McpOptions();
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<McpClientHost>();
    }

    /// <summary>Connects to every enabled server and registers its tools. Idempotent — safe to call once at startup.</summary>
    public async Task ConnectAllAsync(CancellationToken cancellationToken)
    {
        if (_options.Servers.Count == 0)
        {
            _logger.LogDebug("No MCP servers configured.");
            return;
        }

        foreach (var server in _options.Servers)
        {
            if (!server.Enabled)
            {
                _logger.LogInformation("MCP server '{Name}' is disabled — skipping.", server.Name);
                continue;
            }

            try
            {
                await ConnectServerAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to connect MCP server '{Name}' ({Transport}). The server's tools will be unavailable; "
                    + "the rest of the host continues to boot.",
                    server.Name, server.Transport);
            }
        }
    }

    private async Task ConnectServerAsync(McpServerOptions server, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.Name))
        {
            throw new InvalidOperationException("Mcp:Servers[].Name is required.");
        }

        IClientTransport transport = BuildTransport(server);
        var client = await McpClient.CreateAsync(
            transport,
            clientOptions: null,
            loggerFactory: _loggerFactory,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _clients.Add(client);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MCP server '{Name}' connected; discovered {Count} tool(s).", server.Name, tools.Count);

        foreach (var tool in tools)
        {
            var prefixedName = $"{server.Name}.{tool.Name}";
            var schemaJson = tool.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                ? """{"type":"object"}"""
                : tool.JsonSchema.GetRawText();
            var definition = new ToolDefinition(
                Name: prefixedName,
                Description: string.IsNullOrWhiteSpace(tool.Description)
                    ? $"MCP tool '{tool.Name}' from server '{server.Name}'."
                    : tool.Description,
                JsonInputSchema: schemaJson);

            var capturedTool = tool;
            var timeoutSeconds = _options.CallTimeoutSeconds;
            McpToolInvoker invoker = async (args, ct) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                var result = await capturedTool.CallAsync(
                    arguments: args!,
                    cancellationToken: cts.Token).ConfigureAwait(false);
                // CallToolResult.Content is a list of content blocks; concat the textual ones for the LLM.
                return string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text));
            };

            try
            {
                _registry.Register(new McpToolAdapter(definition, invoker));
                _registeredNames.Add(prefixedName);
            }
            catch (ToolException ex)
            {
                _logger.LogWarning(ex,
                    "Could not register MCP tool '{Name}' — a tool with that name is already present.",
                    prefixedName);
            }
        }
    }

    private static IClientTransport BuildTransport(McpServerOptions server)
    {
        if (string.Equals(server.Transport, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(server.Transport, "sse", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Url))
            {
                throw new InvalidOperationException($"MCP server '{server.Name}': Url is required for {server.Transport} transport.");
            }
            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
            });
        }

        if (!string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MCP server '{server.Name}': unknown transport '{server.Transport}'. Expected stdio | http.");
        }

        if (string.IsNullOrWhiteSpace(server.Command))
        {
            throw new InvalidOperationException($"MCP server '{server.Name}': Command is required for stdio transport.");
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Args.Count > 0 ? server.Args.ToArray() : null,
            EnvironmentVariables = server.Env.Count > 0
                ? server.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)
                : null,
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var name in _registeredNames)
        {
            _registry.Unregister(name);
        }
        _registeredNames.Clear();

        foreach (var client in _clients)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose MCP client cleanly."); }
        }
        _clients.Clear();
    }
}
