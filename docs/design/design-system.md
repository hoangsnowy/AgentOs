# AgentOS Design System

> Single source of truth for the AgentOS "Agent Studio" UI. Read this before adding any screen,
> component, or style. If a value isn't here, it goes through a **token** — never a magic number.

## North star

**An operating system for agents — a desktop world, GNOME-on-Linux at heart, with the tactility of
a game.** AgentOS isn't a dashboard with a sidebar; it's a *desktop metaphor*: a wallpaper, a top
bar, a dock, draggable/resizable windows, a Start-menu (Kickoff) launcher, a lock screen. Every
feature is an **app** you launch into a window, not a route you navigate to.

Three feelings, in priority order:

1. **Familiar OS** — anyone who's used GNOME / KDE Plasma / macOS / Win11 knows where things are.
   Top bar = status + clock + user. Dock = launch + running apps. Windows = focus ring, traffic
   lights, drag, resize, minimize.
2. **Calm enterprise** — Breeze-leaning: low-chroma single accent (`#3daee9`), small radii, flat
   surfaces, restrained shadows. It runs all day without fatigue. Color carries *state*, never
   decoration.
3. **A touch of game** — alive, not sterile: the dock magnifies on hover, windows scale-in, the
   wallpaper drifts, status dots pulse. Motion is a reward, never a blocker (`prefers-reduced-motion`
   kills all of it).

When a design decision is ambiguous, resolve it in that order: **familiar > calm > playful.** A
playful flourish that hurts familiarity or calm loses.

## Where the system lives (the actual source)

| Layer | File | Rule |
|---|---|---|
| **Tokens** | `src/AgentOs.Web/wwwroot/app.css` `:root` block | Every color/space/radius/font/shadow/motion value. Components reference `var(--…)` only. |
| **Theme axes** | same file, `:root[data-theme]` + `:root[data-wallpaper]` | `data-theme` = light \| dark (color). `data-wallpaper` = enterprise-light/dark \| aurora \| midnight \| sunset (bg + glass). Orthogonal — any theme × any wallpaper. |
| **Components** | `src/AgentOs.Web/Components/UI/*.razor` | The vocabulary. Reuse before you invent. |
| **Icons** | `src/AgentOs.Web/Components/UI/Icon.razor` (+ [icon-map.md](icon-map.md)) | One monochrome 24×24 Lucide-style set, `currentColor`. Never inline an `<svg>` or emoji in a page. |
| **Theme JS** | `src/AgentOs.Web/wwwroot/theme.js` | `agenticTheme.*` — persists + applies theme/wallpaper/glass to `<html>` data-attrs. |
| **KC login skin** | `infra/keycloak/themes/agentos/login/` | Mirrors these tokens so sign-in matches the shell. |

## Tokens (the contract)

Don't memorize values — reference these names. Full list in `app.css`; the families:

- **Surfaces**: `--bg`, `--bg-2`, `--bg-3`, `--bg-sunk`, plus role aliases `--surface-0..3` and the
  chrome surfaces `--bg-topbar/-titlebar/-toolbar/-statusbar/-sidebar`.
- **Borders**: `--line`, `--line-strong` (= `--border-subtle`, `--border-strong`).
- **Text**: `--txt`, `--txt-soft`, `--txt-dim`, `--txt-faint`, `--txt-on-accent` (= `--text-primary`
  … `--text-disabled`). Four levels of emphasis — pick by hierarchy, don't hand-pick greys.
- **Accent**: `--accent` `#3daee9` + `-hover` / `-active` / `-soft`. **One** accent. There is a
  legacy `--accent-2` (violet) — *deprecated*, alias to `--accent`; do not introduce new violet.
- **State**: `--ok` `--warn` `--err` `--idle` (= `--state-success/-warning/-danger/-info`). Color
  = state only.
- **Radii**: `--r-1`(2) `--r-2`(4) `--r-3`(5) `--r-4`(6) `--r-5`(8). Small. Cards 6, pills 999.
- **Spacing**: 4-base — `--space-1`(4) … `--space-8`(48), plus `--s-1..10`. No raw px gaps.
- **Type**: `--font` (Inter), `--mono` (JetBrains Mono). Sizes `--fs-xs`(11) … `--fs-2xl`(28).
  Weights `--fw-regular/medium/semibold` (400/500/600 — never 700+ for UI text; 700 is reserved for
  the brand mark + tiny uppercase labels).
- **Elevation**: `--shadow-0..4` + `--shadow-window-focused/-unfocused` + `--shadow-inset`. Higher =
  more "lifted" (menus, dialogs, focused window). Don't stack ad-hoc box-shadows.
- **Motion**: `--duration-fast`(80ms) `-base`(160) `-slow`(240); `--ease-standard`, `-emphasized`.
  Hover/press = fast. Open/close = base. Never animate layout-affecting props on a timer.
- **Focus**: `--focus-ring`. Every interactive element shows it on `:focus-visible`. Non-negotiable.

## Components (reuse, don't reinvent)

| Component | Use for | Don't |
|---|---|---|
| `Btn` (Primary/Default/Ghost/Danger × Sm/Md/Lg) | Every button | Hand-roll a `<button class>`; raw buttons miss focus ring + states |
| `IconBtn` | Square icon-only action | Put a bare `<Icon>` in a clickable `<span>` |
| `Icon` | All iconography | Inline SVG, emoji, icon fonts |
| `Panel` / `.card` | Titled content block | Nest panels >1 deep |
| `Field` | Label + input + hint wrapper | A naked `<label><input>` |
| `Toggle` | Boolean setting | A checkbox for an on/off *preference* |
| `Dialog` | Modal confirm / form | A window for a yes/no question |
| `Dropdown` | Top-bar flyout (clock, user) | Reuse for in-page selects (use `<select class="prefs-select">`) |
| `ContextMenu` | Right-click menu | A dropdown anchored to the cursor |
| `Badge` / `Chip` / `Grade` | Status pills | Color-only status with no text/icon (a11y) |
| `Toast` (via `ToastService`) | Transient feedback | A dialog for "saved" |
| `Spinner` / `Progress` | Loading / determinate progress | A "Loading…" string |
| `AppFrame` + `WindowManagerService` | **Any new app** = a window | A full-page route; AgentOS apps live in windows |

### Desktop shell anatomy (and who owns what)

- **TopBar** (`Components/UI/TopBar.razor`) — brand · workspace · active-window title · health dot ·
  theme toggle · **clock** · user menu. Status surface, not a toolbar.
- **Dock + Start** (`Taskbar.razor` + `AppShellLayout.razor`) — launch pinned apps, show running
  dots, Kickoff search. Reads `AppCatalog` (respect `AdminOnly` via `VisiblePinned/VisibleAll`).
- **Windows** (`AppFrame` ← `WindowHost` ← `WindowManagerService`) — one entry per open app; z-order
  on focus; optional auto-minimize-on-blur.
- **System app** (`SystemApp.razor`) — the OS settings: General, Appearance, **Date & time**,
  Notifications, About, Session. **All device/shell preferences live here** — never scatter an OS
  setting into a feature page or a top-bar dropdown. (The clock dropdown *deep-links* here via
  `WM.RequestLaunchTab("system","datetime")`; it does not own the setting.)

## Rules (the part that keeps it coherent)

1. **New feature = new app, in a window.** Register it in `AppCatalog`, render it in `WindowHost`,
   gate with `AdminOnly` if needed. Don't add top-level routes/pages.
2. **Settings belong in the System app.** A preference toggled anywhere else is a smell. Surfaces
   may *deep-link* to the right System tab, but the control + persistence live in System.
3. **Tokens or nothing.** No raw hex, px spacing, or px radius in a component. If a needed value
   doesn't exist as a token, add the token first.
4. **One accent, color = state.** Blue accent for interactivity/selection; ok/warn/err for state.
   No decorative color. No second brand hue.
5. **Reuse the component vocabulary.** Before writing markup + CSS, check the table above. New
   shared widget → it goes in `Components/UI/` with tokens, not inline in a page.
6. **Icons through `Icon`.** One set, `currentColor`, sized via `Size=`. New glyph → add a case to
   `Icon.razor` + a row to `icon-map.md`.
7. **Every interactive element: keyboard + focus ring + a11y name.** `:focus-visible` shows
   `--focus-ring`; icon-only controls carry `aria-label`/`Title`; status never color-only.
8. **Motion is opt-out-able and non-blocking.** Use the motion tokens; honor
   `prefers-reduced-motion`; never gate an action behind an animation finishing.
9. **Light + dark, both wallpapers.** Test any new surface on `data-theme` light *and* dark, over a
   light *and* a dark wallpaper. Glass surfaces must stay legible on all four.
10. **KC login mirrors the shell.** A token change that affects sign-in chrome
    (accent, font, card, radius) updates `infra/keycloak/themes/agentos/login/` too.

## Adding a screen — the checklist

- [ ] Is it an **app**? Register in `AppCatalog` + `WindowHost`; don't add a route.
- [ ] Reuses `Btn`/`Field`/`Panel`/`Toggle`/`Dialog`/`Icon` — no hand-rolled equivalents.
- [ ] Only `var(--…)` tokens — zero raw hex/px.
- [ ] One accent; state colors only for state.
- [ ] Keyboard-reachable; `:focus-visible` ring on every control; a11y names on icon-only buttons.
- [ ] Legible in light+dark × light+dark wallpaper; glass stays readable.
- [ ] Settings (if any) live in the System app, deep-linked — not inline.
- [ ] Motion via tokens, `prefers-reduced-motion` respected.

## Known debt (don't widen it)

- `--accent-2` (violet) is deprecated → aliased to `--accent`. Don't add new uses; migrate when you
  touch a file that still references it.
- Two card primitives exist (`.card` legacy, `.panel` preferred) — prefer `Panel`; don't add new
  `.card` usages.
