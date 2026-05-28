# Icon migration map — emoji → `<Icon>`

`Components/UI/Icon.razor` is the single SVG source. Style: Lucide-flavored monochrome, 24×24 viewBox, `currentColor` stroke, 1.75px stroke-width, MIT inspiration. The component is hand-authored — we did not pull a runtime icon package because Blazor Server is the host and a thirty-icon switch beats shipping a font.

**Use** with the friendly name, e.g. `<Icon Name="play" Size="16" />` or with a screen-reader title `<Icon Name="play" Title="Run pipeline" />`.

## Map

| Emoji | `Name` | Used by (file:line) |
| --- | --- | --- |
| 🚀 (Pipeline) | `play` | Desktop.razor:14, AppShellLayout.razor:55,62 |
| 🚀 (Open PR) | `git-pull-request` | Desktop.razor:28, AppShellLayout.razor:62 |
| 🕸 | `graph` | Desktop.razor:16, AppShellLayout.razor:56, Taskbar.razor:64 |
| ⚙ | `gear` | Desktop.razor:18,46, AppShellLayout.razor:57, SystemApp.razor:11, TopBar.razor:60, Taskbar.razor:65 |
| 🛠 | `wrench` | Desktop.razor:20,45, AppShellLayout.razor:41, TopBar.razor:59, Taskbar.razor:66 |
| 🔨 | `hammer` | Desktop.razor:30, AppShellLayout.razor:61 |
| ↗ | `arrow-square-out` | Desktop.razor:32, AppShellLayout.razor:66, Settings.razor:13 |
| ▶ | `play` | Desktop.razor:49, AppShellLayout.razor:55, OrchestrationStudio.razor:51, PipelineStudio.razor:19,60 |
| ⏱ | `clock` | TopBar.razor:47 |
| 🔒 | `lock` | TopBar.razor:62, AppShellLayout.razor:69, SystemApp.razor:89 |
| 🔔 | `bell` | TopBar.razor (notification), AppShellLayout.razor:60, SystemApp.razor:13, Desktop.razor:48 |
| ℹ | `info` | SystemApp.razor:14, AppShellLayout.razor:65 |
| ⏻ | `power` | TopBar.razor:63, AppShellLayout.razor:47,70, SystemApp.razor:15,93 |
| 🎨 | `palette` | SystemApp.razor:12 |
| ⊞ | `squares-four` | Taskbar.razor:11 |
| ⟳ | `arrow-clockwise` | Desktop.razor:44 |
| ◆ | `diamond` | AppShellLayout.razor:28, LoginOverlay.razor:10, TopBar.razor:12 |
| ● | `circle-fill` | Settings.razor:10, TopBar.razor (tb-dot — already SVG-ish via CSS) |
| 💾 | `floppy-disk` | OrchestrationStudio.razor (Save) |
| 🗑 | `trash` | OrchestrationStudio.razor:53 |
| ⚡ | `lightning` | OrchestrationStudio.razor:50 |
| 🌙 | `moon` | OrchestrationStudio.razor:31 (theme toggle) |
| ☀ | `sun` | OrchestrationStudio.razor:31 (theme toggle) |
| 📱 | `device-mobile` | AppShellLayout.razor:38 |
| 🔧 | `wrench` | AppShellLayout.razor:41 (same as 🛠) |
| ❔ | `question` | AppShellLayout.razor:44 |

## Glyphs intentionally kept as text (KDE Breeze chrome convention)

| Glyph | Where | Why |
| --- | --- | --- |
| `—` | AppFrame minimize button | Plasma window action |
| `□` `❐` | AppFrame maximize/restore | Plasma window action |
| `×` | AppFrame + Dialog close | Plasma window action; not an emoji |
| `⤡` | AppFrame resize handle | Diagonal arrow, no Lucide equivalent of equal weight |
| `▸` | Cascading menu chevron | Direction marker; replaced when AppShellLayout cascade is removed (C5) |

## Migration commits

The actual call-site replacement happens in C5 (Desktop + TopBar + Taskbar restyle). Until then `Btn.Icon` / `IconBtn.Icon` / `DesktopIcon.Icon` keep accepting strings — feed them either an emoji (legacy) or an `<Icon Name="…" />` render-fragment via `ChildContent`.
