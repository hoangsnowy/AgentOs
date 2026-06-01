// No-op fallback when no SMTP host is configured (standalone dev with no MailHog, CI). It logs the
// message at Information so the link is still discoverable in the console — mirrors the no-op repo
// pattern used across the modules so the host boots without external services.

using System.Threading;
using System.Threading.Tasks;
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
        _logger.LogInformation(
            "Email sending is not configured (Email:SmtpHost empty) — would have sent '{Subject}' to {To}. Body: {Text}",
            subject, toEmail, textBody ?? "(html only)");
        return Task.CompletedTask;
    }
}
