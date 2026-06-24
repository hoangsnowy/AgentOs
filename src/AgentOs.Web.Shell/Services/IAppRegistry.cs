// The shell's view of the host's app catalog. The windowing chrome (WindowHost) needs to turn a window's
// app key into the Blazor component to render — but the shell must not know any specific app, and the
// host's AppCatalog (with its built-in component types) lives in the host. So the shell depends on this
// interface; the host implements it. This is the seam that lets the chrome live in AgentOs.Web.Shell.

namespace AgentOs.Web.Shell.Services;

/// <summary>Resolves a desktop app key to the component the <c>WindowHost</c> renders. Host-implemented.</summary>
public interface IAppRegistry
{
    /// <summary>The Blazor component type registered for <paramref name="appKey"/>, or <c>null</c> if unknown
    /// (the host renders the "Unknown app" placeholder).</summary>
    System.Type? ResolveComponent(string appKey);
}
