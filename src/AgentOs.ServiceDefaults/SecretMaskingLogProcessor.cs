// Global choke point for credential leakage on the exported log pipeline: every OpenTelemetry log
// record (OTLP / dashboard / App Insights via OTel) has credential-shaped substrings masked before
// it leaves the process. The console formatter is not covered (local dev surface); the shipped
// surfaces are.

using AgentOs.SharedKernel.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace AgentOs.ServiceDefaults;

/// <summary>Masks API keys / bearer tokens / connection-string passwords on every exported log record.</summary>
internal sealed class SecretMaskingLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        if (data is null)
        {
            return;
        }
        if (data.FormattedMessage is { Length: > 0 } message)
        {
            data.FormattedMessage = LogSafe.MaskSecrets(message);
        }
        if (data.Body is { Length: > 0 } body)
        {
            data.Body = LogSafe.MaskSecrets(body);
        }
    }
}
