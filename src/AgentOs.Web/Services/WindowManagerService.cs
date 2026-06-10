// AgentOs.Web/Services/WindowManagerService.cs
// Multi-window state. Each "app click" opens an entry here; WindowHost renders them.
// Z-order is bumped on focus so the most-recently-clicked window paints on top.

using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentOs.Web.Services;

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
    private readonly List<string> _recent = new();
    private const int RecentCapacity = 6;
    private int _nextZ = 100;
    private int _cascadeIndex;

    /// <summary>Read-only snapshot of currently open windows.</summary>
    public IReadOnlyList<AppWindowState> Open => _open;

    /// <summary>Most-recently-opened app keys, newest first, capped at <see cref="RecentCapacity"/>.</summary>
    public IReadOnlyList<string> RecentKeys => _recent;

    /// <summary>System → General → "Auto-minimize on blur": when a window gains focus, the others
    /// are sent to the dock.</summary>
    public bool AutoMinimizeOnBlur { get; set; }

    /// <summary>
    /// One-shot deep-link target: the tab a freshly-opened app should land on. Set it right before
    /// <see cref="OpenApp"/> (e.g. the TopBar clock requests System → "datetime"); the app reads it
    /// once on init via <see cref="ConsumeLaunchTab"/> and it self-clears so a later plain launch
    /// opens the default tab. Keyed by app so concurrent deep-links don't collide.
    /// </summary>
    private readonly Dictionary<string, string> _launchTab = new(StringComparer.Ordinal);

    /// <summary>Request that the next <see cref="OpenApp"/> of <paramref name="appKey"/> lands on <paramref name="tab"/>.</summary>
    public void RequestLaunchTab(string appKey, string tab) => _launchTab[appKey] = tab;

    /// <summary>Read + clear the pending launch tab for an app (null when none was requested).</summary>
    public string? ConsumeLaunchTab(string appKey)
    {
        if (_launchTab.Remove(appKey, out var tab)) { return tab; }
        return null;
    }

    /// <summary>
    /// One-shot launch payload: data a freshly-opened app consumes on init (e.g. the Spine "Open in
    /// Pipeline" action prefills the Pipeline app's story text). Set it right before <see cref="OpenApp"/>;
    /// the app reads it once via <see cref="ConsumeLaunchPayload"/> and it self-clears, so a later plain
    /// launch starts empty. Keyed by app so concurrent deep-links don't collide.
    /// </summary>
    private readonly Dictionary<string, string> _launchPayload = new(StringComparer.Ordinal);

    /// <summary>Request that the next <see cref="OpenApp"/> of <paramref name="appKey"/> receives <paramref name="payload"/>.</summary>
    public void RequestLaunchPayload(string appKey, string payload) => _launchPayload[appKey] = payload;

    /// <summary>Read + clear the pending launch payload for an app (null when none was requested).</summary>
    public string? ConsumeLaunchPayload(string appKey)
    {
        if (_launchPayload.Remove(appKey, out var payload)) { return payload; }
        return null;
    }

    /// <summary>Fires when the window set or z-order changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Fires when the clock-format prefs (24h / seconds) change in System settings, so the TopBar
    /// clock re-reads them live without a reload. Separate from <see cref="Changed"/> to avoid
    /// churning every window subscriber on a clock-only tweak.
    /// </summary>
    public event Action? ClockPrefsChanged;

    /// <summary>Raise <see cref="ClockPrefsChanged"/>. Called by System → Date &amp; time toggles.</summary>
    public void NotifyClockPrefsChanged() => ClockPrefsChanged?.Invoke();

    /// <summary>Open an app window. If one already exists for this key, focus it instead.</summary>
    public AppWindowState OpenApp(string appKey, string title, string icon, int w = 920, int h = 620)
    {
        TrackRecent(appKey);
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

    /// <summary>Bring a window to the top of the z-stack. When <see cref="AutoMinimizeOnBlur"/> is
    /// on, the other windows are minimized to the dock.</summary>
    public void Focus(Guid id)
    {
        var i = _open.FindIndex(x => x.Id == id);
        if (i < 0) { return; }
        _open[i] = _open[i] with { Z = ++_nextZ };
        if (AutoMinimizeOnBlur)
        {
            for (var j = 0; j < _open.Count; j++)
            {
                if (_open[j].Id != id && !_open[j].Minimized)
                {
                    _open[j] = _open[j] with { Minimized = true };
                }
            }
        }
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

    /// <summary>Push an app key onto the recents stack (newest first, deduped, capped).</summary>
    private void TrackRecent(string appKey)
    {
        _recent.Remove(appKey);
        _recent.Insert(0, appKey);
        if (_recent.Count > RecentCapacity)
        {
            _recent.RemoveRange(RecentCapacity, _recent.Count - RecentCapacity);
        }
    }
}
