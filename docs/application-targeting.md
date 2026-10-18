# Application targeting

Available in the source build; the published v2.0.0 release predates this work.

## Choose and preview a target

In **Resize window**, expand **Match this application**. Enter the application's process name, such as `msedge` or `devenv`, to inspect matches without resizing. A trailing `.exe` is accepted. Alternatively, choosing an app in the original window list keeps its existing remembered-size behavior.

Optional conditions narrow that process match:

| Condition | Behavior |
| --- | --- |
| Executable path | Exact full Windows path, ignoring letter case and slash direction. Relative paths, wildcards and `.`/`..` path segments are rejected. |
| Window title | Literal Contains or Exact comparison, ignoring letter case. No wildcard or regular expression interpretation. |
| Window class | Exact comparison, ignoring letter case. |

All nonblank conditions must match. **Use selected window details** fills the available identifiers and uses Exact title matching. Remove or shorten title text when documents, tabs or project names change. Missing path or class metadata never satisfies a requested condition; unavailable metadata is shown in the preview.

The match list shows each window's title and size. Selecting one is read-only: the detail card displays its executable path, class, bounds and minimized state. The large schematic preview shows the requested bounds on the chosen destination display, with the current window bounds where they overlap that display. It does not capture the application's pixels. **Refresh matches** rereads the open windows. A selection and every actual resize recheck window identity and current matching metadata.

**Resize window** applies to the selected matching window. **Save setup as profile** stores the current rule with a new setup. **Update active resize profile** confirms replacement while retaining its name, ID and shortcut. Invalid conditions cannot be saved or applied.

## Profiles and remembered sizes

An applied Resize profile uses a selected matching window, or one unique match if none is selected. Multiple matches without a selected match require a choice; the frontmost window is no longer chosen automatically. Zero matches resize nothing. Shortcut application uses the same rule and ambiguity handling.

Manual resizing remembers the accepted size for the current process and conditions. Different windows of one application can have different remembered sizes. On ordinary app selection, a matching specific rule takes priority over a process-only default. Each filled condition adds specificity; Exact title counts higher than Contains. Equally specific overlapping rules skip automatic resizing and show an explanation. **Forget size** removes the resolved remembered rule, preserving the app's other rules. Explicit selection in the match preview never applies a remembered size.

Existing settings and profiles without conditions retain process-only matches. Profiles, duplicates, last-session state, JSON settings and the database retain the new fields. Targeted profile exports use version 3; files without conditions remain version 2. Versions 1 and 2 still import. Matching conditions in an older-version document are rejected. Full paths and title text are readable in exports; inspect them before sharing. A path from another PC can require editing without broadening the rule automatically.

## Scope and verification

This step adds deliberate targeting and previews. No background trigger applies a setup when an app opens or gains focus. [DisplayFusion's documented triggers](https://www.displayfusion.com/Features/Triggers/) provide the comparison for future opt-in window creation, focus and monitor-profile workflows.

Executable paths use the limited-information process query documented by [Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew). Window classes use [GetClassName](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclassnamew). Access failures leave metadata unavailable; they do not remove a requested filter.

Core checks cover combined conditions, literal title modes, absent metadata, validation, selected versus unique targets, ambiguity, read-only preview selection, changed titles, remembered-rule precedence and isolation, profile saving/updating, versioned packages and both settings stores. These checks do not establish behavior for every elevated, protected or changing-title application.

### Results on 2026-10-18

- 420 Core tests passed, with no failures or skips. Release x64 builds of the app and Windows-check harness completed with no warnings or errors.
- The native metadata check discovered 13 windows, each with an executable path and class. All 13 paths agreed with a separate process-module query. Matching checks verified the discovered identifiers and rejected absent path/class metadata and changed title text. No windows were resized.
- In the Windows UI, entering `msedge` showed two matches. A Contains title condition narrowed this to one. Selecting that match displayed the native path, class and bounds and enabled the resize button while Restore original size remained unavailable. No resize was requested.
- **Use selected window details** filled the exact executable path, full title, Exact title mode and window class. Changing to an unmatched full path showed zero matches and disabled resizing. A relative path showed a validation explanation and kept resizing disabled.
- The existing desktop had 3840 × 2160 displays at 150% and 175%, including a negative display origin. Runtime edits were discarded and the saved setup restored. Native targeted resizing of elevated/protected apps, title changes during an actual resize, and application-open triggers were not exercised. Recorder checks were not rerun.

Run the read-only discovery check on an unlocked Windows desktop with at least one other open application:

```powershell
dotnet build tests/ExactFrame.WindowsChecks/ExactFrame.WindowsChecks.csproj -c Release -p:Platform=x64
& ./tests/ExactFrame.WindowsChecks/bin/x64/Release/net10.0-windows10.0.19041.0/ExactFrame.WindowsChecks.exe --targeting-metadata-only --output artifacts/targeting-checks/local
```

The check writes `windows-checks.json` with counts and results; it retains no window titles, executable paths or screen images. This run's receipts are under ignored `artifacts/targeting-checks/20261018-native/`.
