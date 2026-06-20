# AgentOS — design context for Claude Design

> Paste this whole file into Claude Design at the start of a session (onboarding "add context",
> or the first message). It is the condensed contract from `docs/design/design-system.md`,
> `docs/design/tokens.md`, and the live `:root` block in `src/AgentOs.Web/wwwroot/app.css`.
> Anything you generate must use these token **values**; do not invent new colors/spacing/radii.

## What AgentOS is

An **operating system for agents** — a desktop world (GNOME-on-Linux at heart, with a touch of
game). Not a dashboard with a sidebar: a wallpaper, top bar, dock, draggable/resizable windows,
a Start-menu (Kickoff) launcher, a lock screen. **Every feature is an app launched into a window,
never a route you navigate to.**

Three feelings, in priority order — resolve ambiguity in this order:

1. **Familiar OS** — GNOME / KDE Plasma / macOS / Win11 muscle memory. Top bar = status + clock +
   user. Dock = launch + running apps. Windows = focus ring, traffic lights, drag, resize, minimize.
2. **Calm enterprise** — KDE Breeze-leaning: low-chroma single accent, small radii, flat surfaces,
   restrained shadows. Runs all day without fatigue. **Color carries state, never decoration.**
3. **A touch of game** — dock magnifies on hover, windows scale-in, wallpaper drifts, status dots
   pulse. Motion is a reward, never a blocker (`prefers-reduced-motion` kills all of it).

> **familiar > calm > playful.** A flourish that hurts familiarity or calm loses.

## Tokens — exact values (light = default, dark = `[data-theme="dark"]`)

### Surfaces
| Token | Light | Dark | Use |
|---|---|---|---|
| `--bg` / `--surface-0` | `#eef2f7` | `#1b1e23` | App background |
| `--bg-2` / `--surface-1` | `#ffffff` | `#232629` | Panel / window body |
| `--bg-3` / `--surface-2` | `#f4f6fa` | `#2a2e32` | Nested surface |
| `--bg-sunk` / `--surface-3` | `#e1e7ef` | `#16191c` | Sunken inset |

Chrome surfaces (light): `--bg-topbar #ffffff`, `--bg-titlebar #f4f6fa`, `--bg-toolbar #f7f9fc`,
`--bg-statusbar #eef2f7`, `--bg-sidebar #f7f9fc`.

### Text (4 levels — pick by hierarchy, never hand-pick greys)
| Token | Light | Dark |
|---|---|---|
| `--txt` | `#0f172a` | `#eff0f1` |
| `--txt-soft` | `#334155` | `#bdc3c7` |
| `--txt-dim` | `#64748b` | `#7f8c8d` |
| `--txt-faint` | `#94a3b8` | `#5d646b` |
| `--txt-on-accent` | `#ffffff` | `#ffffff` |

### Borders
`--line` `#cfd6e0` / `#3a3f44` · `--line-strong` `#b6c0cc` / `#4e555c`

### Accent — ONE accent, Breeze blue
| Token | Light | Dark |
|---|---|---|
| `--accent` | `#3daee9` | `#3daee9` |
| `--accent-hover` | `#2a93cc` | `#4cbcf2` |
| `--accent-active` | `#1d7eb3` | `#2196e1` |
| `--accent-soft` | `#d6ecf7` | `#1d3a4d` |

**No second brand hue.** A legacy violet `--accent-2` is deprecated → aliased to `--accent`; never
introduce new violet.

### State (color = state only)
| Token | Light | Dark |
|---|---|---|
| `--ok` | `#16a34a` | `#2ecc71` |
| `--warn` | `#d97706` | `#f39c12` |
| `--err` | `#dc2626` | `#e74c3c` |
| `--idle` | `#cbd5e1` | — |
| `--state-info` | = `--accent` | = `--accent` |

### Radii — small (Breeze). Never exceed 6px on windowed surfaces
`--r-1 2px` · `--r-2 4px` · `--r-3 5px` · `--r-4 6px` · `--r-5 8px` ·
`--radius-sm 4px` · `--radius-md 6px`. Cards = 6, pills = 999.

### Spacing — 4-base, no raw px gaps
`--space-1 4` · `2 8` · `3 12` · `4 16` · `5 24` · `6 32` · `7 40` · `8 48`
(aliases `--s-1..10`).

### Typography
- Sans `--font`: **Inter** → Noto Sans → Segoe UI → system-ui
- Mono `--mono`: **JetBrains Mono** → Cascadia Code → Fira Code → Consolas
- Sizes: `--fs-xs 11` · `sm 12` · `base 13` · `md 14` · `lg 16` · `xl 20` · `2xl 28` (px)
- Line height: `--lh-tight 1.25` · `--lh-normal 1.5`
- Weights: `--fw-regular 400` · `--fw-medium 500` · `--fw-semibold 600`.
  **Never 700+ for UI text** (700 reserved for brand mark + tiny uppercase labels).

### Elevation (don't stack ad-hoc box-shadows)
- `--shadow-1` `0 1px 0 rgba(15,23,42,.04)`
- `--shadow-2` `0 2px 4px rgba(15,23,42,.06), 0 1px 0 rgba(15,23,42,.04)`
- `--shadow-3` `0 8px 20px rgba(15,23,42,.10), 0 2px 4px rgba(15,23,42,.06)`
- `--shadow-4` `0 24px 48px -8px rgba(15,23,42,.18), 0 8px 16px rgba(15,23,42,.08)`
- Focused window: `0 16px 32px -8px rgba(15,23,42,.32), 0 6px 12px rgba(15,23,42,.14), 0 0 0 1px var(--accent)`

### Focus (non-negotiable on every interactive element)
`--focus-ring: 0 0 0 2px rgba(61,174,233,.55)` shown on `:focus-visible`.

### Motion
`--duration-fast 80ms` (hover/press) · `--duration-base 160ms` (open/close) · `--duration-slow 240ms`.
Ease: `--ease-standard cubic-bezier(.2,0,0,1)`, `--ease-emphasized cubic-bezier(.05,.7,.1,1)`.
No overshoot/bounce. Honor `prefers-reduced-motion`.

### Theme axes (orthogonal — any theme × any wallpaper)
- `data-theme` = `light` | `dark` (color tokens)
- `data-wallpaper` = `enterprise-light` (default) | `enterprise-dark` | `aurora` | `midnight` | `sunset`
  (sets `--wallpaper-bg` + `--wallpaper-animation` only)
- Glass: `--glass-blur` (0–32px) + `--glass-saturate` (100–200%) on every `backdrop-filter` site.

## Component vocabulary — reuse, don't reinvent

| Component | Use for | Key props / variants |
|---|---|---|
| `Btn` | Every button | Variant: `Primary` / `Default` / `Ghost` / `Danger`; Size: `Sm` / `Md`; `Loading` (inline spinner, disables), `Icon`, `Disabled` |
| `IconBtn` | Square icon-only action | needs `aria-label`/`Title` |
| `Icon` | All iconography | one monochrome 24×24 Lucide-style set, `currentColor`, `Size=`; never inline `<svg>`/emoji |
| `Panel` / `.card` | Titled content block | `Title`, `HeaderActions`, body; never nest >1 deep; prefer `Panel` over legacy `.card` |
| `Field` | Label + input + hint | `Label`, `Hint`, wraps the input |
| `Toggle` | Boolean preference | (not a checkbox) |
| `Dialog` | Modal confirm / form | (not a window for yes/no) |
| `Dropdown` | Top-bar flyout (clock, user) | in-page selects use `<select class="prefs-select">` |
| `ContextMenu` | Right-click menu | |
| `Badge` / `Chip` / `Grade` | Status pills | always text/icon + color, never color-only |
| `Toast` (`ToastService`) | Transient feedback | not a dialog for "saved" |
| `Spinner` / `Progress` | Loading / progress | not a "Loading…" string |
| `AppFrame` + `WindowManagerService` | **Any new app = a window** | not a full-page route |

### Desktop shell anatomy
- **TopBar**: brand · workspace · active-window title · health dot · theme toggle · clock · user menu.
  A status surface, not a toolbar.
- **Dock + Start (Kickoff)**: launch pinned apps, running dots, search.
- **Windows**: `AppFrame` ← `WindowHost` ← `WindowManagerService`; z-order on focus; traffic lights.
- **System app**: OS settings (General, Appearance, Date & time, Notifications, About, Session).
  **All device/shell preferences live here** — never scatter a setting into a feature page.

## Hard rules (keep generated work coherent)

1. **New feature = new app in a window.** Not a top-level route/page.
2. **Settings live in the System app.** Surfaces may deep-link to it; the control + persistence stay there.
3. **Tokens or nothing.** No raw hex / px spacing / px radius. Need a value? Add a token first.
4. **One accent; color = state.** Blue for interactivity/selection; ok/warn/err for state. No decorative color.
5. **Reuse the component vocabulary** above before writing new markup.
6. **Icons through `Icon`** — one set, `currentColor`, sized via `Size=`.
7. **Every interactive element: keyboard + `:focus-visible` ring + a11y name.** Status never color-only.
8. **Motion is opt-out-able + non-blocking.** Use motion tokens; honor `prefers-reduced-motion`.
9. **Light + dark, both wallpapers.** Stay legible on light/dark theme over light/dark wallpaper; glass readable.
10. **Branding is "AgentOS"** (one word, capital O-S). Never "AgentOs", "Agent Studio", or "AgenticSdlc".
    Code namespaces stay `AgentOs.*`, but the product/UI name is **AgentOS**.

## When generating UI for this project

- Output should look like it belongs in a calm enterprise KDE-Breeze desktop, **not** a colorful
  consumer SaaS landing page. Flat surfaces, thin 1px borders, 4–6px radii, single blue accent.
- Default to **light theme on the enterprise-light wallpaper**, but verify dark works too.
- Reproduce these tokens as CSS variables and reference them — don't hardcode the hex inline.
- Treat any new screen as an **app window** (title bar + body), not a marketing page.
