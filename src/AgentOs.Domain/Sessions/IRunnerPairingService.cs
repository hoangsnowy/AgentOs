// M3 — per-runner pairing secret. A runner (a member's paired dev machine) authenticates to the
// SignalR hub with a high-entropy token issued once at registration. The server stores ONLY a salted
// hash of that token (never the plaintext), exactly the way a PAT is stored: the token is already
// 256 bits of entropy, so a fast salted SHA-256 + constant-time compare is the right primitive — a
// slow KDF (PBKDF2/bcrypt) only buys resistance against low-entropy password guessing we don't have.

namespace AgentOs.Domain.Sessions;

/// <summary>A freshly issued pairing secret. <see cref="Token"/> is shown to the member exactly once
/// (it is never persisted); <see cref="TokenHash"/> is what the runner row stores.</summary>
public sealed record RunnerPairingSecret(string Token, string TokenHash);

/// <summary>Issues + verifies runner pairing tokens. Stateless; register as a singleton.</summary>
public interface IRunnerPairingService
{
    /// <summary>Mint a new high-entropy token and its salted hash. Persist only the hash.</summary>
    RunnerPairingSecret Issue();

    /// <summary>Constant-time check that <paramref name="presentedToken"/> matches a stored
    /// <paramref name="storedHash"/>. Returns false (never throws) on any malformed input.</summary>
    bool Verify(string presentedToken, string storedHash);
}
