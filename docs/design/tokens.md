# Design tokens — AgentOS (enterprise admin console, KDE Plasma Breeze leaning)

All tokens live in `src/AgentOs.Web/wwwroot/app.css` under `:root`. Two orthogonal axes drive the theming:

- `data-theme` on `<html>` — `light` (default, Breeze Light) or `dark` (Breeze Dark). Controls color tokens.
- `data-wallpaper` on `<html>` — `enterprise-light` (default), `enterprise-dark`, `aurora`, `midnight`, `sunset`. Controls only `--wallpaper-bg` and `--wallpaper-animation`.

The slider in System → Appearance writes `--glass-blur` (0–32 px) and `--glass-saturate` (100–200%) inline on `<html>`. Persistence lives in `localStorage` via `wwwroot/theme.js` (`agenticTheme.applyTheme/applyWallpaper/applyGlass`); the script restores all three before Blazor mounts to avoid the dreaded theme flash.

## Surfaces

| Token | Light | Dark | Notes |
| --- | --- | --- | --- |
| `--surface-0` / `--bg` | `#eef2f7` | `#1b1e23` | App background |
| `--surface-1` / `--bg-2` | `#ffffff` | `#232629` | Panel, window body |
| `--surface-2` / `--bg-3` | `#f4f6fa` | `#2a2e32` | Nested surface |
| `--surface-3` / `--bg-sunk` | `#e1e7ef` | `#16191c` | Sunken inset |

## Text

| Token | Light | Dark |
| --- | --- | --- |
| `--text-primary` / `--txt` | `#0f172a` | `#eff0f1` |
| `--text-secondary` / `--txt-soft` | `#334155` | `#bdc3c7` |
| `--text-tertiary` / `--txt-dim` | `#64748b` | `#7f8c8d` |
| `--text-disabled` / `--txt-faint` | `#94a3b8` | `#5d646b` |
| `--txt-on-accent` | `#ffffff` | `#ffffff` |

## Borders

| Token | Light | Dark |
| --- | --- | --- |
| `--border-subtle` / `--line` | `#cfd6e0` | `#3a3f44` |
| `--border-strong` / `--line-strong` | `#b6c0cc` | `#4e555c` |

## Accent (single low-chroma) + states

| Token | Light | Dark |
| --- | --- | --- |
| `--accent` | `#3daee9` (Breeze blue) | `#3daee9` |
| `--accent-hover` | `#2a93cc` | `#4cbcf2` |
| `--accent-active` | `#1d7eb3` | `#2196e1` |
| `--accent-soft` | `#d6ecf7` | `#1d3a4d` |
| `--state-success` / `--ok` | `#16a34a` | `#2ecc71` |
| `--state-warning` / `--warn` | `#d97706` | `#f39c12` |
| `--state-danger` / `--err` | `#dc2626` | `#e74c3c` |
| `--state-info` | = `--accent` | = `--accent` |

`--accent-2` is kept as an alias of `--accent` for back-compat. New code must use `--accent`.

## Focus, elevation, radius, spacing, typography, motion

- **Focus**: `--focus-ring` is a `box-shadow`-friendly 2 px halo built from the accent.
- **Elevation**: `--shadow-0` (none), `--shadow-1`..`--shadow-4` plus `--shadow-window-focused` / `--shadow-window-unfocused` (the latter two are key+ambient + 1 px border in one value).
- **Radius**: `--radius-none`, `--radius-sm` (4 px), `--radius-md` (6 px). Breeze keeps things small; **never** exceed `--radius-md` on windowed surfaces.
- **Spacing**: `--space-0`..`--space-8` on a 4-base scale (0, 4, 8, 12, 16, 24, 32, 40, 48). Existing `--s-1`..`--s-10` aliases stay.
- **Typography**: `--fs-xs|sm|base|md|lg|xl|2xl` (11/12/13/14/16/20/28 px), `--lh-tight|normal` (1.25/1.5), `--fw-regular|medium|semibold` (400/500/600). Default sans = Inter → Noto Sans → Segoe UI; mono = JetBrains Mono → Cascadia Code.
- **Motion**: `--duration-fast|base|slow` (80/160/240 ms), `--ease-standard` (`cubic-bezier(.2, 0, 0, 1)`), `--ease-emphasized` (`cubic-bezier(.05, .7, .1, 1)`). The old `cubic-bezier(.34, 1.56, .64, 1)` overshoot on window-open is gone.

## Glass + wallpaper

`--glass-blur` (default `0px`) feeds every `backdrop-filter: blur(var(--glass-blur))` site. The Appearance slider remaps 0–100 % to 0–32 px. `--glass-saturate` is similarly remapped 100–200 %.

`--wallpaper-bg` is the body-level background. `[data-wallpaper="enterprise-*"]` are flat slate gradients (admin-console default). `[data-wallpaper="aurora"]` keeps the original violet/cyan mesh with `bg-drift` animation. `[data-wallpaper="midnight"]` and `[data-wallpaper="sunset"]` are quieter two-radial variants.

## Accessibility

All five wallpaper/theme combinations are intended to meet WCAG 2.1 AA:

- text against `--surface-0` ≥ 4.5:1 in both themes;
- `--accent` ≥ 3:1 against `--surface-0` for non-text UI (light: 3.05, dark: 4.6);
- focus halo is 2 px and stays visible on every surface.

Contrast values are a static snapshot — `axe` or `pa11y` against `/_/components` is the canonical check.
