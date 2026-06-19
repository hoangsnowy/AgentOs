// ADR-0005 Layer 1 — host-agnostic input hardening for build_verifier. MSBuild executes arbitrary
// tasks/targets during a build, so an LLM-authored project/import/config file is remote code
// execution. The model may contribute SOURCE files only; every build-control file is dropped here
// and never written, and the verifier always synthesizes its own zero-dependency project instead.

using System;
using System.Collections.Generic;
using System.IO;

namespace AgentOs.Modules.Integration;

/// <summary>Classifies a generated file path as a build-control file (rejected) or a plain source/content
/// file (kept). Pure + side-effect-free so the policy is unit-testable without running a build.</summary>
internal static class BuildInputSanitizer
{
    // Extensions whose presence steers MSBuild: project + solution files, imports, response files.
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".vbproj", ".fsproj", ".proj", ".sln", ".slnx", ".targets", ".props", ".rsp",
    };

    // Fixed file names MSBuild/NuGet auto-import or read regardless of extension match.
    private static readonly HashSet<string> BlockedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nuget.config", "global.json", "packages.config",
        "directory.build.props", "directory.build.targets", "directory.packages.props",
        "directory.build.rsp", "msbuild.rsp",
    };

    /// <summary><c>true</c> if writing <paramref name="path"/> would let the model influence the build
    /// graph (a project/solution/import/config/response file). Such files are dropped, never built.</summary>
    internal static bool IsBuildControlFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        var name = Path.GetFileName(path.Trim());
        // Windows silently strips trailing dots and spaces from a file name on write, so "Foo.csproj." or
        // "Directory.Build.props " lands on disk as "Foo.csproj" / "Directory.Build.props" and MSBuild
        // auto-imports it — an RCE bypass of the extension/name check. Normalise them away before classifying.
        name = name.TrimEnd('.', ' ');
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (BlockedFileNames.Contains(name))
        {
            return true;
        }
        var ext = Path.GetExtension(name);
        return ext.Length > 0 && BlockedExtensions.Contains(ext);
    }
}
