# Developing ExactFrame

Start with the [README](../README.md) for the user workflows. This guide covers building, packaging and checking the current source. The features described in the README are included in v2.1.0.

## Build and run

Use Windows 10 version 1809 or later; Windows 11 is recommended. Install the .NET 10 SDK selected by [global.json](../global.json). Preview SDKs are disabled. For Visual Studio, install the **WinUI application development** workload.

From the repository root:

```powershell
dotnet restore ExactFrame.slnx -p:Platform=x64 --locked-mode
dotnet build src/ExactFrame/ExactFrame.csproj -c Release -p:Platform=x64 --no-restore
dotnet run --project src/ExactFrame/ExactFrame.csproj -p:Platform=x64 --no-restore
```

For ARM64, replace `-p:Platform=x64` with `-p:Platform=ARM64`. In Visual Studio, open `ExactFrame.slnx`, select the matching platform and start the app.

The app is an unpackaged WinUI 3 executable rather than an MSIX package. Portable release builds include their .NET and Windows App SDK dependencies, so users do not need to install those runtimes separately.

## Tests and verification limits

Run the Core tests after the restore above:

```powershell
dotnet test tests/ExactFrame.Core.Tests/ExactFrame.Core.Tests.csproj -c Release -p:Platform=x64 --no-restore
```

The Core project and its tests are platform-independent. They cover geometry, settings, profiles, hotkeys and view-model behavior using substitute Windows services. Passing them does not establish native pointer behavior, browser measurements, mixed-DPI behavior or recorder output.

Use the [Windows checks harness](../tests/ExactFrame.WindowsChecks/README.md) for opt-in checks on an unlocked Windows desktop. Follow the [recording compatibility guide](recording-compatibility.md) before marking a capture setup as verified. Keep failed and incomplete attempts with the evidence.

## Portable builds and releases

For a local x64 executable, run:

```powershell
./Build-Portable.cmd
```

The result is `artifacts/win-x64/ExactFrame.exe`. For the ARM64 equivalent, use:

```powershell
dotnet publish src/ExactFrame/ExactFrame.csproj -c Release -r win-arm64 -p:Platform=ARM64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o artifacts/win-arm64
```

To create versioned ZIPs for both architectures, with instructions, the font license and SHA-256 checksums:

```powershell
./scripts/Build-Release.ps1 -LockedRestore
```

Packages go to `artifacts/release/<version>/`. The script refuses to overwrite existing ZIPs. Use a fresh output root when checking packaging again:

```powershell
./scripts/Build-Release.ps1 -LockedRestore -OutputRoot artifacts/package-verification/run-1
```

The repository includes [CI](../.github/workflows/ci.yml) for locked restores, Core tests, Windows-harness compilation and x64/ARM64 packaging. The [release workflow](../.github/workflows/release.yml) checks that a pushed version tag matches the source version, runs that build, verifies package checksums and prepares a **draft** GitHub release. Publishing the draft remains a separate action. Hosted workflow execution and native ARM64 execution are separate from successful local builds; see [the engineering checks](usability-and-engineering.md) for current evidence.

## Project layout

| Project | Responsibility |
|---|---|
| `src/ExactFrame.Core` | Platform-independent geometry, models, persistence, service interfaces and `MainViewModel` |
| `src/ExactFrame` | WinUI 3 views, display preview, floating controls and native Windows services |
| `tests/ExactFrame.Core.Tests` | Core behavior tested with substitute services |
| `tests/ExactFrame.WindowsChecks` | Separate opt-in Windows harness for native measurements, overlays and responsiveness |

Inside the app project:

| Folder | Contents |
|---|---|
| `Views/` | Frame, Style, Profiles and Resize panels, dialogs, size editor and Help |
| `Controls/` | `DisplayMap` preview and `KeyChips` keycaps |
| `Hud/` | Floating control bar |
| `Services/` | Overlay, window measurement, displays, hotkeys, window picker, dialogs and clipboard |
| `Native/` | Border and guide windows, shade panels, message window, tray icon and single-instance hand-off |
| `Interop/` | Win32 interop |
| `Helpers/` | Formatting, Help rendering and app-theme helpers |

`App.xaml.cs` wires up services through dependency injection. The view model depends on interfaces, keeping Core logic testable without Windows.

The in-app help lives in `src/ExactFrame.Core/Help/HelpContent.cs`. Keep its user instructions consistent with the README and feature guides.

## Settings and keyboard behavior

Settings, profiles and app-specific sizes live in the CSharpDB database at `%LOCALAPPDATA%\ExactFrame\exactframe.db`. An existing `settings.json` is imported once and left untouched as a backup; subsequent saves use the database. `IAppDataStore` provides typed collections for persistence.

**Start with Windows** uses a current-user Run entry to launch with `--background`. Only one copy runs per Windows session; launching another copy opens the existing one.

Windows represents AltGr as Ctrl + Alt. ExactFrame checks installed keyboard layouts with `ToUnicodeEx` and avoids registering shortcuts that type characters. For example, AltGr + 2 types @ on a Spanish layout. Such shortcuts appear as **Types … on your keyboard** and are also rejected by the shortcut recorder.

## Native measurement and drawing

All capture coordinates use physical pixels, including displays with negative desktop origins. A frame must fit on one display, or within its work area when **Keep clear of the taskbar** is enabled. Oversized requests are rejected.

The colored border is drawn inside the capture rectangle. An invisible 6 px band outside it makes the edge easier to grab. Frame interiors, guides, labels and dimming layers are click-through; border interaction depends on **Click-through**. Additional frame borders follow the same rule.

**Client area** measurement accounts for title bars drawn inside an app's client area, including browser tabs and custom title bars. `TitleBarDetector` probes `WM_NCHITTEST` responses down the top of the window. In browsers, Client area still includes the toolbar.

**Web page** measurement prefers the rendered document's Windows accessibility bounds, then checks Chromium's `Chrome_RenderWidgetHostHWND` child window or another matching accessibility page element. These interfaces can vary between browser versions. The app reports nearby sizes accepted after physical-pixel rounding and rejects an unusable viewport before moving the target window. See [browser sizing](browser-sizing.md) for CSS, zoom and DPR semantics.

Capture exclusion uses `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE`, requiring Windows 10 version 2004 or later for that exclusion behavior. An accepted API call does not establish that a recorder honors it. The main app window is not excluded. Follow the [saved-recording checks](recording-compatibility.md).

## Windows smoke checks

See [usability, accessibility, responsiveness and automated build checks](usability-and-engineering.md)
for compact-window behavior, remaining desktop acceptance checks and the draft release pipeline.

The Core logic is unit-tested and the Windows app, including XAML, builds against Windows App SDK 2.5.1.
Rendering, pointer input, mixed-DPI behavior and recorder output need a Windows run. Follow the
[direct frame editing checks](direct-frame-editing.md#windows-interaction-checks) for the new editor:

- Show a 1280 × 720 outline at 100% and 150% scaling; confirm in your recorder that its outer bounds are exactly
  1280 × 720. Drag the border, then turn on click-through and confirm clicks pass through the border.
- Try each line style, color, guide and dimming level; check the size label flips inside the frame at the top
  edge of a display.
- Confirm the on-screen bar appears under the frame, shrinks when idle, and never steals focus.
- Resize Notepad or a browser in both Client area and Whole window modes, including from maximized and
  minimized; then use **Restore original size**.
- In Client area mode, check that the tab strip of Chrome and Edge (and Edge with vertical tabs) sits outside the
  outline and the toolbar inside it. Repeat with VS Code, Windows Terminal and Notepad at 100% and 150% scaling.
- Resize a visible Chrome or Edge page in Web page mode. Verify the page area, rather than the browser window,
  matches the requested physical pixels. Try an app without a usable native or accessibility page area and verify it is not moved.
- After resizing, drag the app to another position and display, resize it by hand, minimize and restore it,
  then close it. Check that the outline follows, hides and returns at the measured capture bounds.
- Resize an app, restart ExactFrame, then select the app again. Check that the remembered measurement mode
  and size apply automatically; use **Forget size** and confirm selection no longer resizes it.
- Repeat on a second monitor with different scaling and a negative origin.
- Record a new shortcut, and save, apply and delete a profile.
- Save a Resize window profile, move and resize the app by hand, then press the profile's shortcut from another app and
  check the app is resized. Close the app and press it again: nothing else should be resized.
- Add a Spanish or German keyboard layout. Check that Ctrl + Alt + 2 shows **Types “@” on your keyboard** (Spanish) and that
  AltGr + 2 still types @ while ExactFrame runs. Change the profile keys to Ctrl + Shift and check they register.
- Close the window: ExactFrame stays in the notification area, shows the notice once, and Ctrl + Alt + F8 still works.
  Select the icon to reopen it; use **Quit ExactFrame** from its menu. Start ExactFrame twice: one copy, brought to front.
- Turn on **Start with Windows**, sign out and in, and check ExactFrame starts with only the icon. Turn it off in Windows
  Settings > Apps > Startup and check the switch shows off.
- Add a 9:16 guide inside a 16:9 frame; move and resize the main outline, adjust the guide's scale and position,
  copy both bounds, and confirm a saved profile restores the guide. Check the nested outline in a recorder preview.
- Check capture exclusion with the recorder and capture method you actually use.
- Connect to a password-protected OBS WebSocket server. Preview and apply a main and nested frame to a visible,
  unlocked, unfiltered Display Capture in the selected scene, then undo. Check the OBS preview and crop/transform
  values on displays with different scaling and negative origins. Confirm that changing the frame or OBS layout
  requires a new preview, and that active recording/streaming, filtered sources and unknown monitors are rejected.

## API references

- [SetWindowPos](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [UpdateLayeredWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
- [SetWindowDisplayAffinity](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [Title bar customization in WinUI](https://learn.microsoft.com/windows/apps/develop/title-bar)
- [Windows App SDK deployment for unpackaged apps](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
