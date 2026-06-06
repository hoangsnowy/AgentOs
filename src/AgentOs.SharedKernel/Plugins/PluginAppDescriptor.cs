// A desktop app/window a plugin contributes. The plugin registers one (or more) of these in DI; the
// host collects them at startup and adds them to the desktop catalog. ComponentType must be a Blazor
// component (IComponent) in the plugin assembly — the shell renders it via <DynamicComponent>.

using System;

namespace AgentOs.SharedKernel.Plugins;

/// <summary>Describes a plugin-contributed desktop window.</summary>
/// <param name="Key">Stable app key (unique across built-ins + plugins).</param>
/// <param name="Title">Window + launcher title.</param>
/// <param name="Icon">Icon name (must exist in the shell's icon map).</param>
/// <param name="Caption">One-line launcher description.</param>
/// <param name="ComponentType">The Blazor component type to render in the window.</param>
/// <param name="AdminOnly">When true, only shown to tenant admins.</param>
/// <param name="Width">Default window width.</param>
/// <param name="Height">Default window height.</param>
public sealed record PluginAppDescriptor(
    string Key,
    string Title,
    string Icon,
    string Caption,
    Type ComponentType,
    bool AdminOnly = false,
    int Width = 920,
    int Height = 620);
