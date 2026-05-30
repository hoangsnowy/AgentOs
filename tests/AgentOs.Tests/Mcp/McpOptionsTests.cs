// Epic E3 — McpOptions binds from configuration in the same shape an appsettings.json would
// deliver. Validates defaults, multiple servers, env + args round-trip.

using System.Collections.Generic;
using AgentOs.Modules.Mcp.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Mcp;

public sealed class McpOptionsTests
{
    [Fact]
    public void Bind_EmptySection_ReturnsDefaults()
    {
        var opts = BuildOptions(new Dictionary<string, string?>());

        opts.Servers.Count.ShouldBe(0);
        opts.CallTimeoutSeconds.ShouldBe(60);
    }

    [Fact]
    public void Bind_TwoServers_ParsesNameTransportArgsEnv()
    {
        var opts = BuildOptions(new Dictionary<string, string?>
        {
            ["Mcp:CallTimeoutSeconds"] = "30",
            ["Mcp:Servers:0:Name"] = "github",
            ["Mcp:Servers:0:Transport"] = "stdio",
            ["Mcp:Servers:0:Command"] = "npx",
            ["Mcp:Servers:0:Args:0"] = "-y",
            ["Mcp:Servers:0:Args:1"] = "@modelcontextprotocol/server-github",
            ["Mcp:Servers:0:Env:GITHUB_TOKEN"] = "ghp_xxx",
            ["Mcp:Servers:1:Name"] = "remote-things",
            ["Mcp:Servers:1:Transport"] = "http",
            ["Mcp:Servers:1:Url"] = "https://mcp.example.com/sse",
            ["Mcp:Servers:1:Enabled"] = "false",
        });

        opts.CallTimeoutSeconds.ShouldBe(30);
        opts.Servers.Count.ShouldBe(2);

        opts.Servers[0].Name.ShouldBe("github");
        opts.Servers[0].Transport.ShouldBe("stdio");
        opts.Servers[0].Command.ShouldBe("npx");
        opts.Servers[0].Args.ShouldBe(["-y", "@modelcontextprotocol/server-github"]);
        opts.Servers[0].Env["GITHUB_TOKEN"].ShouldBe("ghp_xxx");
        opts.Servers[0].Enabled.ShouldBeTrue();

        opts.Servers[1].Name.ShouldBe("remote-things");
        opts.Servers[1].Transport.ShouldBe("http");
        opts.Servers[1].Url.ShouldBe("https://mcp.example.com/sse");
        opts.Servers[1].Enabled.ShouldBeFalse();
    }

    private static McpOptions BuildOptions(IDictionary<string, string?> kv)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(kv).Build();
        var services = new ServiceCollection();
        services.AddOptions<McpOptions>().Bind(config.GetSection(McpOptions.SectionName));
        return services.BuildServiceProvider().GetRequiredService<IOptions<McpOptions>>().Value;
    }
}
