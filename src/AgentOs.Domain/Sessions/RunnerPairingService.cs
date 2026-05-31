// M3 — default pairing service. Token = 32 random bytes (hex, URL-safe so it drops straight into the
// hub query string). Hash format = "sha256$<saltHex>$<hashHex>" with a per-token 16-byte salt.
// Verify recomputes the hash and compares with CryptographicOperations.FixedTimeEquals so a mismatch
// leaks no timing signal.

using System;
using System.Security.Cryptography;
using System.Text;

namespace AgentOs.Domain.Sessions;

/// <summary>Salted-SHA-256 pairing service. See <see cref="IRunnerPairingService"/>.</summary>
public sealed class RunnerPairingService : IRunnerPairingService
{
    private const string Scheme = "sha256";
    private const int TokenBytes = 32;
    private const int SaltBytes = 16;

    /// <inheritdoc />
    public RunnerPairingSecret Issue()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes));
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Hash(salt, token);
        var stored = string.Concat(Scheme, "$", Convert.ToHexString(salt), "$", Convert.ToHexString(hash));
        return new RunnerPairingSecret(token, stored);
    }

    /// <inheritdoc />
    public bool Verify(string presentedToken, string storedHash)
    {
        if (string.IsNullOrEmpty(presentedToken) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 3 || !string.Equals(parts[0], Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromHexString(parts[1]);
            expected = Convert.FromHexString(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Hash(salt, presentedToken);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Hash(byte[] salt, string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var buffer = new byte[salt.Length + tokenBytes.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(tokenBytes, 0, buffer, salt.Length, tokenBytes.Length);
        return SHA256.HashData(buffer);
    }
}
