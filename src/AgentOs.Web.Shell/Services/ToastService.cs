// AgentOs.Web.Shell/Services/ToastService.cs
// In-memory toast notification service. Components subscribe to Changed; pages call Show(...).

using System;
using System.Collections.Generic;

namespace AgentOs.Web.Shell.Services;

/// <summary>Toast notification kinds — drives colour + icon.</summary>
public enum ToastKind { Info, Ok, Warn, Err }

/// <summary>A single toast notification.</summary>
public sealed record Toast(Guid Id, string Title, string Message, ToastKind Kind, DateTime CreatedAt)
{
    public static Toast Create(string title, string message, ToastKind kind = ToastKind.Info)
        => new(Guid.NewGuid(), title, message, kind, DateTime.UtcNow);
}

/// <summary>Singleton notification bus. The host (AppShellLayout) listens; any page can push.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _items = new();

    /// <summary>Read-only current queue.</summary>
    public IReadOnlyList<Toast> Items => _items;

    /// <summary>Master switch (System → Notifications → "Show toasts"). When off, nothing is shown.</summary>
    public bool ShowToasts { get; set; } = true;

    /// <summary>Focus mode (System → General). When on, suppresses non-critical (Info/Ok) toasts;
    /// warnings and errors still surface.</summary>
    public bool FocusMode { get; set; }

    /// <summary>Fires when the queue mutates so the host can re-render.</summary>
    public event Action? Changed;

    /// <summary>Push a new toast — unless suppressed by <see cref="ShowToasts"/> / <see cref="FocusMode"/>.</summary>
    public void Show(string title, string message, ToastKind kind = ToastKind.Info)
    {
        if (!ShowToasts) { return; }
        if (FocusMode && kind is ToastKind.Info or ToastKind.Ok) { return; }
        _items.Add(Toast.Create(title, message, kind));
        Changed?.Invoke();
    }

    /// <summary>Dismiss a toast by id.</summary>
    public void Dismiss(Guid id)
    {
        _items.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }
}
