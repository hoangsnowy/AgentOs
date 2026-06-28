// AgentOs.Web.Shell/Services/CircuitSafe.cs
// The ONE place a Blazor UI handler is allowed to catch System.Exception.
//
// LLM, Keycloak-admin, config-resolve and persistence calls reach UI handlers through layers
// (Anthropic/Azure SDK, Polly, EF, DataProtection) that throw types deriving from System.Exception
// — LlmException, HttpRequestException, TimeoutRejectedException, DbUpdateException, … An unhandled
// throw on a circuit's synchronization context tears down the whole circuit (blank screen for the
// user). Handlers therefore must surface ANY failure as a message/fallback, never let it escape.
//
// Narrowing to concrete types would reintroduce that crash class for the next unanticipated throw,
// so the broad catch is intentional. Centralising it here keeps it to a single audited, documented
// site instead of one #pragma per handler.

using System;
using System.Threading.Tasks;

namespace AgentOs.Web.Shell.Services;

/// <summary>Runs UI-handler work behind the single documented circuit-protecting catch-all.</summary>
public static class CircuitSafe
{
    /// <summary>
    /// Awaits <paramref name="body"/>. On ANY exception, invokes <paramref name="onError"/> with it
    /// and returns <see langword="false"/>; otherwise returns <see langword="true"/>. Never throws —
    /// the circuit survives. The continuation stays on the caller's context (no <c>ConfigureAwait</c>)
    /// so field writes + the trailing re-render behave exactly like an inline try/catch.
    /// </summary>
    public static async Task<bool> RunAsync(Func<Task> body, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(onError);
#pragma warning disable CA1031 // Intentional: a UI handler must surface ANY failure, never tear down the Blazor circuit.
        try { await body(); return true; }
        catch (Exception ex) { onError(ex); return false; }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Evaluates <paramref name="factory"/> and returns its result, or <paramref name="fallback"/> on
    /// ANY exception. For synchronous fire-and-fallback resolves (e.g. a tenant-scoped config lookup
    /// whose transient fault must degrade to appsettings, not crash the circuit on window open).
    /// </summary>
    public static T OrDefault<T>(Func<T> factory, T fallback)
    {
        ArgumentNullException.ThrowIfNull(factory);
#pragma warning disable CA1031 // Intentional: degrade to the fallback on ANY failure, never tear down the Blazor circuit.
        try { return factory(); }
        catch (Exception) { return fallback; }
#pragma warning restore CA1031
    }
}
