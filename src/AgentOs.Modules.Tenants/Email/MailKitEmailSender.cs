// Real SMTP sender via MailKit. Dev points at MailHog (localhost:1025, no auth/TLS); production
// points at a real provider (SendGrid / SES / Mailgun / etc.) with auth + STARTTLS, credentials
// supplied from secrets. The TLS mode is inferred: UseStartTls → STARTTLS; port 465 → implicit TLS;
// otherwise plaintext (MailHog). Failures are logged and rethrown — callers decide whether the
// send is best-effort (e.g. invitation mail still returns the link for manual sharing).

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.SharedKernel.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AgentOs.Modules.Tenants.Email;

/// <inheritdoc cref="IEmailSender"/>
public sealed class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<EmailOptions> options, ILogger<MailKitEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        if (!string.IsNullOrWhiteSpace(textBody))
        {
            builder.TextBody = textBody;
        }
        message.Body = builder.ToMessageBody();

        var socketOptions = _options.UseStartTls
            ? SecureSocketOptions.StartTls
            : _options.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.None;

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOptions, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(_options.User))
            {
                await client.AuthenticateAsync(_options.User, _options.Password, ct).ConfigureAwait(false);
            }
            await client.SendAsync(message, ct).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, ct).ConfigureAwait(false);
            _logger.LogInformation("Email '{Subject}' sent to {To} via {Host}:{Port}",
                LogSafe.Scrub(subject), LogSafe.MaskEmail(toEmail), _options.SmtpHost, _options.SmtpPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email '{Subject}' to {To} via {Host}:{Port}",
                LogSafe.Scrub(subject), LogSafe.MaskEmail(toEmail), _options.SmtpHost, _options.SmtpPort);
            throw;
        }
    }
}
