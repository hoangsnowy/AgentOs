// ADR-0005 — shared constants for both sandbox runners (in-process + container). The dotnet build
// invocation and the hardening environment are identical regardless of host; only the isolation
// boundary around them differs.

using System.Collections.Generic;

namespace AgentOs.Modules.Integration.Sandbox;

internal static class SandboxDefaults
{
    /// <summary>`dotnet build` argument vector. Quiet, no incremental state, no surviving MSBuild node.</summary>
    internal static readonly string[] DotnetBuildArgv =
        ["build", "--nologo", "-v", "q", "--no-incremental", "-nodeReuse:false"];

    /// <summary>Space-joined form for <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/>.</summary>
    internal const string DotnetBuildArgs = "build --nologo -v q --no-incremental -nodeReuse:false";

    /// <summary>Environment that hardens the dotnet/MSBuild child: no telemetry/first-run writes, no node
    /// reuse, no persistent MSBuild server. HOME is set per-runner (scratch dir / container path).</summary>
    internal static readonly IReadOnlyList<KeyValuePair<string, string>> HardeningEnv =
    [
        new("DOTNET_CLI_TELEMETRY_OPTOUT", "1"),
        new("DOTNET_NOLOGO", "1"),
        new("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"),
        new("MSBUILDDISABLENODEREUSE", "1"),
        new("DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER", "1"),
    ];
}
