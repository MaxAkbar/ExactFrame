# ExactFrame

A Windows utility for setting up screen recordings. Show a pixel-exact outline of the area you'll record,
or resize another app so its content is an exact size.

Version 2.0 is a WinUI 3 rewrite of the Windows Forms app. It adds a
to-scale display preview, a 3 × 3 anchor grid, outline styles and guides, dimming outside the frame,
saved profiles with hotkeys, and a floating control bar next to the outline.

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

## Using ExactFrame

**Outline area.** Pick a size preset, drag the width and height sliders, or type exact values (the chain button keeps the aspect ratio). The sliders run up to the largest size that fits the display, snap to even pixels, and pull toward standard sizes such as 1280, 1920 and 2560 so they're easy to land on. Arrow keys move a focused slider 2 px at a time and Page Up / Page Down 64 px.
Choose where the frame sits with the anchor grid, or type X and Y. Choose a display under the preview.
Select **Show outline** (Ctrl + Alt + F8). Drag the border to move it; Esc hides it while ExactFrame has focus.

**Style tab.** Outline color, weight (2, 4 or 6 px) and line (solid, dashed or corners only), the size label,
dimming outside the frame, rule-of-thirds, center-mark and 90% safe-area guides, and the on-screen controls.

**Profiles tab.** **Save current** stores the size, position, display and mode. The first nine profiles get
Ctrl + Alt + 1 to 9. **Global hotkeys** shows each shortcut; if another app owns one, it says so and
**Change** records a new one.

**Resize window.** Choose an app from the list or use **Pick on screen** and click it (right-click or Esc
cancels). Choose whether the size applies to the **Client area** (content only) or the **Whole window**, and
whether to center it or keep its current position. Select **Resize window**. A minimized or maximized app is first restored to a normal window, then brought to the front so it lands visibly inside the outline. (A minimized app has no position to keep, so it's centered.) ExactFrame moves the window,
measures its real borders on that monitor, adjusts, and verifies the result. **Restore original size** puts
it back as it was before the first resize in this session.

**On-screen controls.** While the outline is visible, a small bar sits under it (or above it when there's no
room) with the size, position, center, guides, click-through, hide-from-recorders, open ExactFrame and hide.
It shrinks to the size readout after 3 seconds without the pointer nearby. It never takes focus from the app
you're recording, and it's hidden from recorders along with the outline.

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
| Ctrl + Alt + 1 … 9 | Apply profile 1 to 9 |
| Esc | Close help, or hide the outline (while ExactFrame has focus) |

Settings and profiles are saved to `%LOCALAPPDATA%\ExactFrame\settings.json`.

## How the outline works

- All sizes are **physical pixels**. A 1920 × 1080 frame stays 1920 × 1080 at 100%, 150% or 200% scaling.
- The colored border is drawn **inside** the frame, so the frame's outer edges are the capture rectangle.
  A 6 px invisible band just outside the frame makes the edge easy to grab; it's outside the recorded area.
- The middle of the frame is click-through. With **Click-through** on, the border is too.
- Dimming, guides and the size label are click-through layers. Guides are drawn inside the frame, so keep
  **Hide from recorders** on while they're visible.
- **Hide from recorders** uses `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE`. Recorder support
  varies: check your recorder's preview, and hide the outline with Ctrl + Alt + F8 if it shows up. The main
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
| `Services/` | `OutlineOverlay`, `WindowService`, `DisplayService`, `HotkeyService`, `WindowPicker`, dialogs and clipboard |
| `Native/` | Win32 windows: the draggable border, guide layer, shade panels and the hidden message window |
| `Interop/` | The P/Invoke boundary |

`App.xaml.cs` is the composition root (Microsoft.Extensions.DependencyInjection). The view model only
depends on interfaces, so every behavior can be tested without Windows.

## Windows smoke checks

The Core logic is unit-tested and the app's C# was compile-checked against the Windows App SDK 2.5.1
projections. XAML compilation, rendering and native behavior need a Windows run:

- Show a 1280 × 720 outline at 100% and 150% scaling; confirm in your recorder that its outer bounds are exactly
  1280 × 720. Drag the border, then turn on click-through and confirm clicks pass through the border.
- Try each line style, color, guide and dimming level; check the size label flips inside the frame at the top
  edge of a display.
- Confirm the on-screen bar appears under the frame, shrinks when idle, and never steals focus.
- Resize Notepad or a browser in both Client area and Whole window modes, including from maximized and
  minimized; then use **Restore original size**.
- Repeat on a second monitor with different scaling and a negative origin.
- Record a new shortcut, and save, apply and delete a profile.
- Check capture exclusion with the recorder and capture method you actually use.

## API references

- [SetWindowPos](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [UpdateLayeredWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
- [SetWindowDisplayAffinity](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [Title bar customization in WinUI](https://learn.microsoft.com/windows/apps/develop/title-bar)
- [Windows App SDK deployment for unpackaged apps](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
