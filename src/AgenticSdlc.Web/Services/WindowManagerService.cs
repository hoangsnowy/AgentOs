// AgenticSdlc.Web/Services/WindowManagerService.cs
// Multi-window state. Each "app click" opens an entry here; WindowHost renders them.
// Z-order is bumped on focus so the most-recently-clicked window paints on top.

using System;
using System.Collections.Generic;
using System.Linq;

namespace AgenticSdlc.Web.Services;

/// <summary>An open application window.</summary>
public sealed record AppWindowState(
    Guid Id,
    string AppKey,
    string Title,
    string Icon,
    int X, int Y, int W, int H,
    int Z,
    bool Minimized);

/// <summary>Singleton state for the desktop window manager. Components subscribe to Changed.</summary>
public sealed class WindowManagerService
{
    private readonly List<AppWindowState> _open = new();
    private int _nextZ = 100;
    private int _cascadeIndex;

    /// <summary>Read-only snapshot of currently open windows.</summary>
    public IReadOnlyList<AppWindowState> Open => _open;

    /// <summary>Fires when the window set or z-order changes.</summary>
    public event Action? Changed;

    /// <summary>Open an app window. If one already exists for this key, focus it instead.</summary>
    public AppWindowState OpenApp(string appKey, string title, string icon, int w = 920, int h = 620)
    {
        var existing = _open.FirstOrDefault(x => x.AppKey == appKey);
        if (existing is not null)
        {
            Restore(existing.Id);
            Focus(existing.Id);
            return existing;
        }

        // Cascade so multiple windows don't stack exactly.
        var offset = (_cascadeIndex++ % 6) * 32;
        var window = new AppWindowState(
            Id: Guid.NewGuid(),
            AppKey: appKey,
            Title: title,
            Icon: icon,
            X: 120 + offset,
            Y: 60 + offset,
            W: w, H: h,
            Z: ++_nextZ,
            Minimized: false);
        _open.Add(window);
        Changed?.Invoke();
        return window;
    }

    /// <summary>Close a window by id.</summary>
    public void Close(Guid id)
    {
        if (_open.RemoveAll(x => x.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Bring a window to the top of the z-stack.</summary>
    public void Focus(Guid id)
    {
        var i = _open.FindIndex(x => x.Id == id);
        if (i < 0) { return; }
        _open[i] = _open[i] with { Z = ++_nextZ };
        Changed?.Invoke();
    }

    /// <summary>Update window geometry (drag / resize).</summary>
    public void Move(Guid id, int x, int y)
    {
        var i = _open.FindIndex(w => w.Id == id);
        if (i < 0) { return; }
        _open[i] = _open[i] with { X = Math.Max(0, x), Y = Math.Max(0, y) };
        Changed?.Invoke();
    }

    /// <summary>Resize a window (corner drag).</summary>
    public void Resize(Guid id, int w, int h)
    {
        var i = _open.FindIndex(x => x.Id == id);
        if (i < 0) { return; }
        _open[i] = _open[i] with { W = Math.Max(280, w), H = Math.Max(200, h) };
        Changed?.Invoke();
    }

    /// <summary>Minimize a window (hidden from view, still in taskbar).</summary>
    public void Minimize(Guid id)
    {
        var i = _open.FindIndex(w => w.Id == id);
        if (i < 0) { return; }
        _open[i] = _open[i] with { Minimized = true };
        Changed?.Invoke();
    }

    /// <summary>Un-minimize a window.</summary>
    public void Restore(Guid id)
    {
        var i = _open.FindIndex(w => w.Id == id);
        if (i < 0) { return; }
        if (_open[i].Minimized)
        {
            _open[i] = _open[i] with { Minimized = false };
            Changed?.Invoke();
        }
    }
}
