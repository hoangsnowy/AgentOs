// Cross-cutting log hygiene. User-controlled values (emails, role/tenant names, model ids, free text)
// must be neutralised before they reach a log sink: newlines let an attacker forge extra log lines
// (CRLF / log-forging), and raw PII (email addresses) should not be persisted to logs in the clear.

using System;

namespace AgentOs.SharedKernel.Logging;

/// <summary>Helpers that make a user-controlled value safe to write to a log sink.</summary>
public static class LogSafe
{
    /// <summary>Removes CR/LF and other control characters so a tainted value cannot inject forged
    /// log lines (log-forging). Returns <c>"(null)"</c> for a null input.</summary>
    public static string Scrub(string? value)
    {
        if (value is null)
        {
            return "(null)";
        }

        // Build only when a control char is actually present — the common case allocates nothing.
        var needsScrub = false;
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                needsScrub = true;
                break;
            }
        }
        if (!needsScrub)
        {
            return value;
        }

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }
        return new string(chars);
    }

    /// <summary>Masks the local part of an email so logs keep enough to diagnose (first char + domain)
    /// without storing the full address in the clear, e.g. <c>alice@example.com</c> → <c>a***@example.com</c>.
    /// Non-email input is scrubbed and returned as-is.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "(none)";
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            // Not an address shape — still neutralise newlines before logging.
            return Scrub(email);
        }

        var domain = Scrub(email[(at + 1)..]);
        var first = Scrub(email[..1]);
        return $"{first}***@{domain}";
    }
}
