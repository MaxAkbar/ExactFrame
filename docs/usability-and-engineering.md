# Usability and engineering checks

Use the **Theme** button in the top toolbar to choose **System**, **Light** or **Dark**. System follows the Windows app theme; Light and Dark override it for ExactFrame. The choice applies immediately and is remembered across restarts, independently of capture profiles. Windows contrast themes take priority, using system colors. The capture preview still represents the chosen outline colors; it is not a simulation of a contrast theme in the recorded app.

The main window can shrink to 640 × 480 Windows logical pixels, subject to the current display's work area. Below 1100 logical pixels wide it opens with controls filling the body. **Show preview** switches to the preview; **Show controls** returns to editing. In wider windows, **Hide preview** gives the controls more room. A short window scrolls the preview instead of shrinking the map away. Large Windows text increases the space reserved for controls and activates compact layout earlier. Toolbar and footer actions remain horizontally scrollable if their text needs extra room.

Help replaces its sidebar with a search box and topic chooser in a narrow window. F1 opens Help and focuses search; Escape closes Help or cancels an active frame gesture. Tab and Shift+Tab reach the toolbar, editing controls and footer. Changing layout does not resize the target application.

## Desktop acceptance checklist

Use a disposable target window when exercising resize commands. Record Windows version, display scaling, Windows **Text size** percentage, theme and reader version. Display scaling and text size are different settings. Restore the original Windows appearance settings after testing.

| Check | Expected result | Current evidence |
|---|---|---|
| Regular and compact window, down to 640 × 480 logical px | Controls scroll, actions remain reachable, preview can be opened and scrolled | Native desktop checks; see local receipt |
| Keyboard navigation | Visible focus; Tab/Shift+Tab reach offscreen actions; Space activates preview switch; F1 opens searchable Help | Focused native checks; full control inventory remains a manual acceptance check |
| App System / Light / Dark choices | Readable text and caption buttons; choice applies while open and survives restart | Native Light/Dark switching, both setup-dialog appearances, Light after restart, and keyboard selection of System checked. Changing the Windows app theme while System is selected remains pending. |
| Text size 150%, 200%, 225% | No overlapping labels, unreachable values or clipped primary actions; compact Help works | Layout reacts to the Windows text-size event; actual enlarged-text acceptance pending |
| Aquatic, Desert, Dusk, Night sky, customized contrast colors | Text/background pairs stay readable, focus and selection remain distinguishable | System-color dictionaries implemented; actual contrast-theme acceptance pending |
| Narrator and another screen reader | Names, values, selection and toggle states are announced; preview instructions are discoverable; modal focus returns correctly | UI Automation labels inspected; actual spoken output pending |
| Preview editing | Show/hide preview, cancel a gesture, change displays, select controls and reopen Help | Repeat after layout/theme changes; real recorder output is covered separately |

For each theme and text size, test Outline area → Frame, Style and Profiles, then Resize window including **Match this application**, Help search, Check my setup, OBS and output-preview dialogs. Exercise the smallest window and a maximized window. In a compact preview, scroll down to the display selector. UI Automation inspection alone does not establish screen-reader compatibility.

Contrast resources and their paired system colors follow [Microsoft's contrast-theme guidance](https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes). Desktop contrast-change notifications use [Windows App SDK ThemeSettings](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.system.themesettings), rather than the UWP-only event that failed during an early local launch check. Text-size layout updates use [UISettings.TextScaleFactorChanged](https://learn.microsoft.com/uwp/api/windows.ui.viewmanagement.uisettings.textscalefactorchanged).

## Responsiveness measurements

On an unlocked desktop, run three measured repeats with one build first:

```powershell
./scripts/Run-ResponsivenessChecks.ps1
# Optional: -BrowserWindowTitle 'Exact native title of the public test browser window'
```

The runner preserves build logs, source and executable hashes, every raw report and a per-repeat summary. It refuses an existing output directory. To invoke one already-built Release, x64 harness run directly:

```powershell
& ./tests/ExactFrame.WindowsChecks/bin/x64/Release/net10.0-windows10.0.19041.0/ExactFrame.WindowsChecks.exe `
  --responsiveness-only --output artifacts/responsiveness/run-1
```

Wait for the fixture to close before reading `windows-checks.json`. Use a **new** output directory for every attempt: a new database is required for the cold-load measurement. The performance harness writes only its own `performance-settings.db`; it does not use personal settings or change another application's bounds. Screen samples used by the magnifier remain in memory.

Fixed order: idle for 3 seconds; production client tracking for 5 seconds; optional browser page tracking for 5 seconds; 50 production magnifier updates separated by 100 ms; fresh-database load; first save after load; 40 saves separated by 50 ms. A separate nominal 16 ms dispatcher timer records gaps, including the first gap across a synchronous operation. Each scenario retains every operation duration and dispatcher gap, CPU time and allocated bytes. Tracking observations identify whether measurement was available. The budgets (50 ms tracking, 100 ms magnifier, 16 ms settings) are diagnostic reference intervals, not acceptance guarantees. Windows timers can run more slowly than requested, so compare with the measured idle baseline.

For browser timing, open a disposable public page in a normal visible Chrome or Edge window, then append `--performance-browser-title 'the exact native window title'`. Exactly one browser window must match. This option only measures/follows that window; it does not resize it, navigate it or save its title or page content. Confirm `observationKinds` contains `Visible` before treating the operation timings as successful page measurements. An omitted browser phase is **not tested**.

Run three times in new directories with builds and recording stopped. Preserve all attempts, including setup errors, and report each repeat separately. Distinguish the fresh-database startup path from routine saves: production loads settings before the main window is activated. A cold database cost does not establish a pause on a normal editing action. Measurements from this one machine do not establish performance under larger profiles, slow disks, browser movement or every display configuration.

### Local measurements, 18 October 2026

Three fixed-order repeats on Windows 11 (build 26300), x64, .NET 10.0.13; connected displays at 150% and 175%. No builds or UI automation ran during the measured phases. Ordinary background applications remained open. Raw evidence, input hashes and the complete per-repeat summary are retained locally in `artifacts/polish/20261018-controlled/`.

| Operation duration | Repeat 1 | Repeat 2 | Repeat 3 |
|---|---:|---:|---:|
| Native client tracking, p95 (81 polls each) | 0.23 ms | 0.35 ms | 0.24 ms |
| Magnifier, p95 (50 updates each) | 14.37 ms | 15.02 ms | 15.02 ms |
| Fresh database load (one per repeat) | 251.44 ms | 258.92 ms | 253.54 ms |
| First save after load (one per repeat) | 7.86 ms | 7.66 ms | 8.02 ms |
| Repeated saves, p95 (40 each) | 5.53 ms | 5.44 ms | 4.44 ms |

All native tracking observations were `Visible`. The nominal 16 ms heartbeat's idle p95 was 31.57–31.74 ms; do not interpret that timer as a 60 fps rendering test. Maximum dispatcher gaps during magnifier work were 46.09–46.64 ms. Routine-save maxima were 5.43–8.15 ms, while the fresh-database load delayed the dispatcher for 252.48–260.17 ms. This identifies startup work to revisit; these bounded routine-save results do not justify claiming an editing performance failure or replacing the persistence contract in this pass.

Browser page tracking remains **not tested**. The tool-created public browser tab was not exposed as a native Chrome/Edge window, and native focus could not be established through that browser tool. Use the opt-in title argument when a suitable visible native test window is available. The earlier exploratory run, `artifacts/polish/20261018-responsiveness/`, overlapped packaging and used a cold first save rather than production's load-then-save order. It is retained separately and excluded from this table; it was not retried to select a better result.

## Reproducible build and draft release

`global.json` already pinned stable .NET SDK 10.0.402 with prerelease SDKs disabled before this pass. NuGet sources and dependency hashes are now checked in. Normal builds and each single-file runtime have separate lock files because publishing adds linker/runtime packages.

`.github/workflows/ci.yml` runs on Windows for pull requests and `main`, and can be dispatched manually. It installs the SDK from `global.json`, restores in locked mode, runs Core tests, compiles the native harness and packages x64 plus ARM64. The harness needs an interactive desktop and is not executed on the hosted runner. Reports, portable ZIPs and SHA-256 checksums are retained as workflow artifacts. Actions are pinned to commit hashes; update pins deliberately.

`.github/workflows/release.yml` runs for a pushed version tag. It requires the tag to equal `v` plus `Directory.Build.props`' version, runs the same CI workflow, downloads the resulting packages, verifies both checksums and creates a **draft** GitHub release. Review compatibility evidence and packages before publishing the draft.

Local packaging uses the same script:

```powershell
./scripts/Build-Release.ps1 -LockedRestore
# For another verification run without replacing existing release assets:
./scripts/Build-Release.ps1 -LockedRestore -OutputRoot artifacts/package-verification/run-1
```

To intentionally change dependencies, restore with the intended build/publish properties without locked mode and review all affected lock-file changes. Keep the normal, portable outer-build, win-x64 and win-arm64 graphs current. ZIP timestamps, hosted-runner images and signing can differ: this pipeline pins build inputs and verifies packaged outputs, but does not claim byte-for-byte reproducibility. ARM64 cross-publishing is not an ARM64 execution test. Check the [Actions history](https://github.com/MaxAkbar/ExactFrame/actions) for hosted CI and draft-release results for a specific commit; a local build does not establish those results.
