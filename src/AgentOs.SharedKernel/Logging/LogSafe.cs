// Cross-cutting log hygiene. User-controlled values (role/tenant names, model ids, free text) must be
// neutralised before they reach a log sink: embedded newlines let an attacker forge extra log lines
// (CRLF / log-forging). Sensitive values (e.g. email addresses) should simply not be logged.

using System;

namespace AgentOs.SharedKernel.Logging;

/// <summary>Helpers that make a user-controlled value safe to write to a log sink.</summary>
public static class LogSafe
{
    /// <summary>Removes CR/LF (and tabs) so a tainted value cannot inject forged log lines
    /// (log-forging). Returns <c>"(null)"</c> for a null input.</summary>
    public static string Scrub(string? value) =>
        value is null
            ? "(null)"
            : value
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal)
                .Replace("\t", " ", StringComparison.Ordinal);
}
