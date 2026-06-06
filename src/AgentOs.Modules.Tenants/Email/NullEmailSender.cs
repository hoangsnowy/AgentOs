// No-op fallback when no SMTP host is configured (standalone dev with no MailHog, CI). It logs the
// message at Information so the link is still discoverable in the console — mirrors the no-op repo
// pattern used across the modules so the host boots without external services.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.SharedKernel.Logging;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Tenants.Email;

/// <inheritdoc cref="IEmailSender"/>
public sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger) => _logger = logger;

    /// <inheritdoc />
    public Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
    {
        // Recipient address is PII — deliberately not logged. The body (which carries the invite link)
        // is enough for the dev to act on; subject correlates it.
        _logger.LogInformation(
            "Email sending is not configured (Email:SmtpHost empty) — would have sent '{Subject}'. Body: {Text}",
            LogSafe.Scrub(subject), LogSafe.Scrub(textBody ?? "(html only)"));
        return Task.CompletedTask;
    }
}
