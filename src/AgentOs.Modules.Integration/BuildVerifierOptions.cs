// Gate for the build verifier. `dotnet build` runs LLM-generated project files, and MSBuild executes
// arbitrary tasks/targets during a build — i.e. a malicious .csproj/.targets is remote code execution on
// the host. Until builds run in a real sandboxed runner (the dev-machine runner model), this is OFF BY
// DEFAULT IN PRODUCTION so an untrusted tenant cannot execute code on the server; Development defaults to
// on. `Integration:BuildVerifier:Enabled` overrides either way.

namespace AgentOs.Modules.Integration;

/// <summary>Runtime gate for <see cref="BuildVerifier"/>. When <see cref="Enabled"/> is false the verifier
/// refuses to spawn a build and returns a disabled result.</summary>
public sealed record BuildVerifierOptions(bool Enabled);
