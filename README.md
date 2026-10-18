# ExactFrame

A Windows utility for setting up screen recordings. Show a pixel-exact outline of the area you'll record,
or resize another app so its content is an exact size.

Version 2.0 is a WinUI 3 rewrite of the Windows Forms app. It adds a
to-scale display preview, a 3 × 3 anchor grid, outline styles and guides, dimming outside the frame,
saved profiles with hotkeys, and a floating control bar next to the outline.
It can also show several capture guides at once, such as a vertical Shorts frame inside a landscape frame.

## Download and start

[Download ExactFrame 2.0.0](https://github.com/MaxAkbar/ExactFrame/releases/tag/v2.0.0).
Choose the package for your PC:

| Windows PC | Download |
|---|---|
| Intel or AMD, 64-bit (most PCs) | [Windows x64 portable ZIP](https://github.com/MaxAkbar/ExactFrame/releases/download/v2.0.0/ExactFrame-2.0.0-win-x64-portable.zip) |
| Windows on Arm | [Windows ARM64 portable ZIP](https://github.com/MaxAkbar/ExactFrame/releases/download/v2.0.0/ExactFrame-2.0.0-win-arm64-portable.zip) |

1. Extract the ZIP to a folder you want to keep, then open **ExactFrame.exe**.
   No installer, .NET installation or Windows App SDK installation is needed.
2. Choose a size and select **Show outline**, or use **Resize window** to size another app's content.
3. Set that area in your recorder. ExactFrame prepares recording areas; it does not record video.

Press **F1** for help. Closing the window keeps ExactFrame in the notification area by default;
right-click its icon and choose **Quit ExactFrame** to exit fully. If you enable **Start with Windows**,
keep the executable in a stable location. Settings are stored in your Windows user profile, rather than
beside the executable.

The release builds are not code-signed. Windows may show an unknown-publisher warning.
The release includes `SHA256SUMS.txt` to verify the downloaded ZIP files, and its notes describe validation
and known limitations. [Report a problem](https://github.com/MaxAkbar/ExactFrame/issues).

## Requirements

- Windows 10 version 1809 or later; Windows 11 recommended. Hiding the outline from recorders needs
  Windows 10 version 2004 or later.
- To build: the **.NET 10 SDK**. For Visual Studio, use a version with the **WinUI application development**
  workload.

The app is **unpackaged** (a plain `.exe`, no MSIX or certificate) and **self-contained**: the Windows App SDK
runtime is copied next to the exe, so users don't need to install it.

## Build and run

```powershell
dotnet run --project src/ExactFrame/ExactFrame.csproj -p:Platform=x64
```

Or open `ExactFrame.slnx`, pick **x64** (or **ARM64**) and press F5.

Run the unit tests (they run on any OS):

```powershell
dotnet test tests/ExactFrame.Core.Tests/ExactFrame.Core.Tests.csproj
```

### Portable build

Double-click `Build-Portable.cmd`. It publishes a single self-contained `artifacts\win-x64\ExactFrame.exe`
that runs without installing .NET or the Windows App SDK. For ARM64, replace `win-x64` and `x64` with
`win-arm64` and `ARM64`.

To build versioned release ZIPs for both architectures, including instructions, the font license and
SHA-256 checksums, run `./scripts/Build-Release.ps1` in PowerShell. Output goes to
`artifacts/release/<version>/`. The SDK is pinned in `global.json`; preview SDKs are not selected.

## Using ExactFrame

**Outline area.** Pick a size preset, drag the width and height sliders, or type exact values (the chain button keeps the aspect ratio). The sliders run up to the largest size that fits the display, snap to even pixels, and pull toward standard sizes such as 1280, 1920 and 2560 so they're easy to land on. Arrow keys move a focused slider 2 px at a time and Page Up / Page Down 64 px.
Choose where the frame sits with the anchor grid, or type X and Y. Choose a display under the preview.
Select **Show outline** (Ctrl + Alt + F8). Drag the border to move it or an edge/corner handle to resize it.
In the preview, drag inside the frame to move it, use its handles to resize, or drag outside it to draw a new frame.
Focus the preview and use arrow keys for 1 physical pixel, Shift + arrows for 10, or Ctrl + arrows to resize from
the right/bottom edges. Keyboard adjustments bypass snapping. Esc or right-click cancels an active drag;
otherwise Esc hides the outline while ExactFrame has focus. Turn click-through off to edit with the pointer or preview keys.

**Draw on screen** lets you drag a new rectangle on any connected display. **Use window bounds** starts with an
existing window's visible outer bounds and leaves that window's size alone. These freeform selections turn the
aspect lock off; turn the chain button back on to preserve the new ratio while resizing. Frames must be at least
100 × 100 physical pixels and fit inside one display's usable area. **Snap to edges and centers** aligns moves and
handle resizes with the display and nearby window bounds. Numeric entry remains available for exact adjustments.
See the [direct frame editing guide and Windows checks](docs/direct-frame-editing.md).

**Additional frames.** In the Frame tab, add a **9:16**, **1:1**, **4:5** or custom guide inside the main frame.
Name each composition, choose an output size, and edit its crop ratio and scale. Drag a subframe in the large preview
or drag its live border on screen. The selected subframe also moves with arrow keys (Shift for 10 px); Alt + drag
in the preview moves the main frame underneath it. Width and height sliders sit beside the exact output-size boxes.
The small position map and parent-relative X/Y remain available. Edge inset and safe margins are adjustable. The crop follows the
main frame when it moves or resizes. **Preview outputs** compares each composition and reports enlargement
or empty bars; **Refresh from screen** takes an in-memory snapshot of the visible source. These previews do
not configure or verify recorder output. **Copy bounds** still copies every source region for your recorder.
See [output compositions](docs/output-compositions.md) for examples, compatibility and verification limits.

**Style tab.** Outline color, weight (2, 4 or 6 px) and line (solid, dashed or corners only), the size label,
dimming outside the frame, rule-of-thirds, center-mark and 90% safe-area guides, and the on-screen controls.

**Profiles tab.** **Save current** creates a named setup with its size, position, display, mode, aspect lock and additional frames.
Check **Include appearance when saving or updating** to save its outline style too. The profile's **More actions** menu offers
**Update from current**, **Rename**, **Duplicate**, **Export**, **Shortcut** and **Delete**. Updating is explicit; saving an existing name
never replaces it. Numbered shortcuts stay with their profiles when another profile is deleted. New saves take the first free
Ctrl + Alt + 1 to 9 slot; duplicates and imports start without a shortcut. **Change** next to **Apply profile 1 to 9** picks other held keys.
**Export all** and **Import** transfer portable profile files. Import reviews display mappings and adds new copies without applying them.
See [portable profiles](docs/portable-profiles.md) for what is included and how positions move between displays.
Applying a Resize window profile resizes its matching app straight away, including from its shortcut while another app has focus.
If several windows match, ExactFrame uses an explicitly selected matching window or asks you to choose one; it no longer
chooses the frontmost window automatically. If nothing matches, nothing is resized. **Global hotkeys** shows each shortcut; if another app owns one, it says so and **Change** records a
new one.

Windows sends AltGr as Ctrl + Alt, so on many keyboards a Ctrl + Alt shortcut is a character: AltGr + 2 is @ on a Spanish
keyboard and AltGr + 7 is { on a German one. ExactFrame asks each installed keyboard layout what a shortcut types
(`ToUnicodeEx`) and leaves any that type a character unregistered, marked **Types … on your keyboard**, so typing keeps
working. It also refuses to record such a shortcut.

**Running in the background.** ExactFrame has an icon in the notification area: select it to open the window, or right-click
it to show or hide the outline, apply a profile or quit. With **Keep running when closed** on (the default), closing the
window leaves ExactFrame running so shortcuts and the outline keep working; the first time, a notification says so.
**Start with Windows** adds a Run entry for the current user that starts ExactFrame with `--background`, in the notification
area only. Only one copy runs per Windows session: starting it again opens the running copy instead.

**Resize window.** Choose an app from the list or use **Pick on screen** and click it (right-click or Esc
cancels). Choose whether the size applies to the **Client area**, the **Whole window**, or the **Web page**, and
whether to center it or keep its current position. Select **Resize window**. A minimized or maximized app is first restored to a normal window, then brought to the front so it lands visibly inside the outline. (A minimized app has no position to keep, so it's centered.) ExactFrame moves the window,
measures its real borders on that monitor, adjusts, and verifies the result. **Restore original size** puts
it back as it was before the first resize in this session.
**Client area** leaves out the title bar, including one the app draws itself inside its client area: the tab strip in
Chrome and Edge, or the title bar in VS Code and Windows Terminal. ExactFrame finds where it ends by asking the app which
part of the window is at points down its top edge (`WM_NCHITTEST`), the question Windows asks when you drag or snap it.
In a browser, Client area includes the toolbar. **Web page** measures the visible page viewport in supported Chromium windows
such as Chrome and Edge, excluding tabs and toolbars. It prefers the rendered document's Windows accessibility bounds,
then checks Chromium's `Chrome_RenderWidgetHostHWND` child window or another matching accessibility page element.
These browser interfaces are implementation details. At some display scales Chromium rounds the viewport to nearby
physical pixels; ExactFrame reports the size it accepted.
If the selected app does not expose one usable visible viewport, ExactFrame reports that
before moving the window; use **Client area** instead. Restore a minimized browser before using this mode.
ExactFrame remembers the measured size and measurement mode for each app after a
manual resize. The next time you select that app from the list or pick it on screen, ExactFrame applies its
saved size automatically. It keeps the app's current position when that size fits, otherwise centers it on
the app's display. If the saved size cannot fit, it leaves the app alone and shows a warning. Select
**Forget size** beside the app to remove its saved size; the next manual resize saves a new one.
If you choose **Web page** before selecting an app with a remembered Client area size, that choice applies to the
current resize while retaining the remembered dimensions.

Open **Match this application** to narrow profiles and remembered sizes by full executable path, window title
(Contains or Exact), and window class. All nonblank conditions must match. Enter an application process name or use
**Use selected window details**, then review the matching windows and selected window's title, path, class and bounds.
Selecting a match here only previews it; **Resize window** applies the size. **Save setup as profile** retains the rule,
and **Update active resize profile** replaces an existing resize setup after confirmation. Specific remembered rules
take priority over a process-only default; equally specific conflicting rules skip automatic resizing.
See [application targeting](docs/application-targeting.md) for behavior, transfer compatibility and checks.

After resizing, the outline follows that app when it moves or changes size, including across displays. Move
the app itself; its outline is click-through while following. The outline hides if the app is minimized or
hidden and returns when it becomes visible. Hiding the outline, choosing another app, editing the requested
frame or restoring the original size stops following.

**OBS Studio (development build).** This integration is in the current source; it is not part of the 2.0.0
download above. In OBS Studio 28 or later, open **Tools > WebSocket Server Settings**, enable the server,
and keep authentication enabled. Select **Send to OBS** in ExactFrame, enter the server port (normally 4455)
and password, then connect. The integration connects only to OBS on this PC. The password is cleared after
connecting and is never saved.

Choose the main frame or an additional guide, an OBS scene, and an existing **Display Capture** placed
directly in that scene. The item must be visible, unlocked and unfiltered. ExactFrame identifies its Windows
monitor, converts desktop coordinates to source-pixel crop margins, and rejects unknown displays or mismatched
source dimensions. Negative monitor origins and Windows display scaling are handled in physical pixels.

Choose **Fit to canvas** for uniform scaling with space around the frame when aspect ratios differ, or
**Actual pixels** to center without scaling. Select **Preview change** to review the proposed canvas geometry,
crop, scale and OBS output dimensions, then **Apply to OBS**. The preview is a geometry drawing, not a captured
image. Applying rechecks the source and verifies the resulting transform. It changes only that scene item's
crop and placement, resetting its rotation, flip and bounding box. It does not alter video settings, switch
scenes, create sources or start recording. Stop recording and streaming before applying or undoing, and check
the OBS preview before recording. The output dimensions shown are OBS's global output setting; an encoder's
separate recording rescale setting can further change the final file dimensions.

**Undo last apply** restores the previous crop and transform while the connection stays open, and refuses
to overwrite later edits in OBS. Closing the dialog keeps the connection and undo history available;
disconnecting or quitting ExactFrame clears them. Frame changes invalidate a prepared preview; they do not
automatically update OBS. Window Capture, grouped sources, enabled source filters and remote OBS instances
are not supported in this first integration.

**Check my setup (development build).** The title bar opens a short workflow for checking a saved test clip
and copying a local setup report. Windows accepting capture exclusion does not verify the recording. The
floating control bar now reports a failed request and retries it. See the [recording compatibility matrix](docs/recording-compatibility.md)
for recorder methods, browser versions, display scaling, monitor transitions and evidence limits, plus
[repeatable Windows checks](tests/ExactFrame.WindowsChecks/README.md). Results start unverified; completed
checklists are explicitly user-reported. None of these results establish support for an untested setup.

**On-screen controls.** While the outline is visible, a small bar sits under it (or above it when there's no
room) with the size, position, center, guides, click-through, hide-from-recorders, open ExactFrame and hide.
It shrinks to the size readout after 3 seconds without the pointer nearby. It never takes focus from the app
you're recording. Windows is asked to exclude it along with the outline; check a saved test clip for your capture method.

**Help.** Select **?** in the title bar or press F1. Help opens at the topic for what you're doing, and has
a search box, a live list of your shortcuts (including any another app has taken), troubleshooting, and an
**About** page with version details, **Open settings folder** and **Copy system info** for bug reports.
Esc or **Close** returns to the main view. The help text lives in `src/ExactFrame.Core/Help/HelpContent.cs`;
keep it in step with this README.

| Shortcut | Action |
|---|---|
| F1 | Open help |
| Ctrl + Alt + F8 | Show or hide the outline |
| Ctrl + Alt + F9 | Turn click-through on or off |
| Ctrl + Alt + F10 | Apply the next profile |
| Ctrl + Alt + 1 … 9 | Apply profile 1 to 9 (the held keys can be changed) |
| Esc | Close help, or hide the outline (while ExactFrame has focus) |

Settings, profiles and app-specific sizes are saved in the CSharpDB database at
`%LOCALAPPDATA%\ExactFrame\exactframe.db`. On first launch after this update, ExactFrame imports an existing
`settings.json` once and leaves that file untouched as a backup. Later saves go only to the database.
Future features can save typed records in separate collections through `IAppDataStore` without adding more
settings files.

## How the outline works

- All sizes are **physical pixels**. A 1920 × 1080 frame stays 1920 × 1080 at 100%, 150% or 200% scaling.
- The colored border is drawn **inside** the frame, so the frame's outer edges are the capture rectangle.
  A 6 px invisible band just outside the frame makes the edge easy to grab; it's outside the recorded area.
- The middle of the frame is click-through. With **Click-through** on, the border is too.
- Additional frame interiors are click-through. Their borders can be dragged when **Click-through** is off; locking makes those borders click-through too. Colored borders stay inside their exact capture bounds.
- Dimming, guides and the size label are click-through layers. Guides are drawn inside the frame, so keep
  **Hide from recorders** on while they're visible.
- **Hide from recorders** uses `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE`. Recorder support
  varies: check a saved test clip, and hide the outline with Ctrl + Alt + F8 if it shows up. The main
  window isn't excluded; minimize it or move it off the recording area.
- A frame must fit on its display (or inside the work area with **Keep clear of the taskbar**). ExactFrame
  rejects an oversized frame instead of shrinking it.

## Solution layout

| Project | Responsibility |
|---|---|
| `src/ExactFrame.Core` | Platform-independent: geometry, models, settings store, service interfaces and `MainViewModel`. No Windows references. |
| `src/ExactFrame` | WinUI 3 app: XAML views, the to-scale `DisplayMap`, the HUD window, and the Win32 services behind the Core interfaces. |
| `tests/ExactFrame.Core.Tests` | xUnit tests for geometry (all the original GeometryChecks), settings, hotkeys and view-model behavior with fakes. |

Inside the app project:

| Folder | Contents |
|---|---|
| `Views/` | The Frame, Style, Profiles and Resize panels, the size editor, and the Help page |
| `Controls/` | `DisplayMap` (preview) and `KeyChips` (keycaps) |
| `Hud/` | The floating control bar window |
| `Services/` | `OutlineOverlay`, `WindowService` (with `TitleBarDetector`), `DisplayService`, `HotkeyService`, `WindowPicker`, dialogs and clipboard |
| `Native/` | Win32 windows: the draggable border, guide layer, shade panels, the hidden message window, the notification-area icon and the single-instance hand-off |
| `Interop/` | The P/Invoke boundary |

`App.xaml.cs` is the composition root (Microsoft.Extensions.DependencyInjection). The view model only
depends on interfaces, so every behavior can be tested without Windows.

## Windows smoke checks

The Core logic is unit-tested and the Windows app, including XAML, builds against Windows App SDK 2.5.1.
Rendering, pointer input, mixed-DPI behavior and recorder output need a Windows run. Follow the
[direct frame editing checks](docs/direct-frame-editing.md#windows-interaction-checks) for the new editor:

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
