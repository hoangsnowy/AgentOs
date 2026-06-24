// Guardrail for the app-agnostic WindowHost: it renders whatever ComponentType the AppCatalog registry
// hands it (via DynamicComponent), with no per-app switch. So EVERY registered app must declare a real
// Blazor component — a null/wrong ComponentType would render the "Unknown app" blank at runtime. This
// test fails the build instead, the moment a new app forgets its component.

using AgentOs.Web.Services;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Web;

public sealed class AppCatalogTests
{
    [Fact]
    public void EveryRegisteredApp_DeclaresARenderableBlazorComponent()
    {
        foreach (var app in AppCatalog.All)
        {
            app.ComponentType.ShouldNotBeNull(
                $"app '{app.Key}' has no ComponentType — the WindowHost would render it as the blank 'Unknown app' window");
            typeof(IComponent).IsAssignableFrom(app.ComponentType!).ShouldBeTrue(
                $"app '{app.Key}' ComponentType '{app.ComponentType!.Name}' is not a Blazor IComponent");
        }
    }
}
