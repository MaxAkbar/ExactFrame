# ExactFrame

Get your recording area right before you press Record.

ExactFrame helps you frame a tutorial, demo or screen recording at the size you need. Draw an outline around a **1920 × 1080** area, make a browser window fit that space, or add a vertical guide to see how the same shot will work as a Short.

Use it alongside OBS Studio or your usual recorder. ExactFrame helps you set up the shot; your recorder captures the video.

## Download and open

**[Download ExactFrame 2.1.0](https://github.com/MaxAkbar/ExactFrame/releases/tag/v2.1.0)** for Windows:

| Your PC | Download |
|---|---|
| Intel or AMD, 64-bit — most Windows PCs | [x64 portable ZIP](https://github.com/MaxAkbar/ExactFrame/releases/download/v2.1.0/ExactFrame-2.1.0-win-x64-portable.zip) |
| Windows on Arm | [ARM64 portable ZIP](https://github.com/MaxAkbar/ExactFrame/releases/download/v2.1.0/ExactFrame-2.1.0-win-arm64-portable.zip) |

Extract the ZIP, then open **ExactFrame.exe**. There is no installer and no separate .NET installation to do. You need **Windows 10 version 1809 or later**; Windows 11 is recommended.

The downloads are currently unsigned, so Windows may show an unknown-publisher warning. Release notes include known limitations and a `SHA256SUMS.txt` file for checking your download.

> **Version 2.1.0** includes the frame editor, OBS integration, portable profiles, application matching and theme choices described below. See the [release notes](docs/releases/v2.1.0.md) for changes and known limitations.

## Set up your first recording

For a fixed region of your desktop:

1. Choose **Outline area** and pick a size, such as **1080p**, or type your own width and height.
2. Pick a display under the preview. Use the position grid to center the frame or place it in a corner.
3. Select **Show outline**, or press **Ctrl + Alt + F8**. Arrange your content inside the border.
4. Select **Copy bounds** and use the X, Y, width and height values to set your recorder's region or crop.
5. Save a short test recording and check its framing before recording the full take.

A **1920 × 1080** frame means 1920 × 1080 physical screen pixels, even when Windows display scaling is set to 150% or 200%. The colored border sits inside the frame; its outer edges mark the capture area.

**Hide from recorders** asks Windows to leave the outline and its controls out of captures. It needs Windows 10 version 2004 or later and a compatible capture method, so check the saved clip. If the outline appears, hide it with **Ctrl + Alt + F8** before recording. Move or minimize the main ExactFrame window if it is inside the shot.

## Make an app fit your recording

Choose **Resize window**, select an app from the list or use **Pick on screen**, then enter the size you want. Choose what that size should measure:

| Mode | What fits inside your requested size |
|---|---|
| **Client area** | The app's content below its title bar. In a browser, this includes the toolbar. |
| **Whole window** | The entire window, including its borders and title bar. |
| **Web page** | The visible page in a supported browser such as Chrome or Edge, leaving tabs and toolbars outside. |

Select **Resize window** to apply it. ExactFrame measures the result and tells you whether the app reached the exact size or accepted a nearby size. Some apps enforce minimum sizes or round their dimensions.

After a resize, the outline follows the app as you move it, including between displays. **Restore original size** returns the window to how it was before the first resize in that session.

ExactFrame also remembers an app's last measured size and mode. Selecting that app again can apply the remembered size automatically. Use **Forget size** to stop this.

For web development, keep in mind that these are **physical pixels**. ExactFrame does not measure the page's CSS viewport, browser zoom or device-pixel ratio. The [browser sizing guide](docs/browser-sizing.md) explains how to check those values in your browser.

## Make the setup your own

- **Keep useful guides on screen.** Add a 9:16, 1:1, 4:5 or custom frame inside the main one to plan different crops of the same recording.
- **Adjust the outline.** The **Style** tab has colors, line weights, dashed or corner borders, dimming, and composition guides.
- **Save setups you use often.** In **Profiles**, save a size, position, display and mode together. You can include the appearance and apply a profile with a shortcut.
- **Keep controls close by.** The floating bar beside the outline lets you make quick adjustments. **Click-through** lets clicks pass through the border to the app underneath.
- **Leave ExactFrame running.** By default, closing the window keeps it in the notification area so the outline and shortcuts stay available. Right-click its icon and choose **Quit ExactFrame** to exit. If you enable **Start with Windows**, keep the executable in a stable folder.

A frame must fit on one display. If there is not enough room, ExactFrame explains the problem instead of silently shrinking the requested size.

## More ways to work

When you need more than a basic recording region:

| If you want to… | Use… |
|---|---|
| Draw a region on screen, start from an existing window, or resize with handles | [Direct frame editing](docs/direct-frame-editing.md), including snapping and precise keyboard adjustments |
| Move additional frames directly and plan their final output sizes | [Output compositions](docs/output-compositions.md), with crop controls, size sliders and output previews |
| Move your saved setups to another PC | [Profile import and export](docs/portable-profiles.md), with a review of display mappings before import |
| Choose the right app when several similar windows are open | [Application matching](docs/application-targeting.md) by executable path, title or window class, with a preview of matches |
| Apply a frame to an OBS Display Capture source | [Send to OBS](docs/obs-integration.md), with a preview of the change and undo |
| Check whether your recording setup behaves as expected | **Check my setup** and the [recording compatibility guide](docs/recording-compatibility.md) |
| Choose an appearance or work in a smaller window | **Theme → System, Light or Dark**, plus [compact controls and a collapsible preview](docs/usability-and-engineering.md) |

Output previews help you plan a composition. A saved clip is still the way to check what your recorder actually produced. The compatibility guide separates completed checks from setups that remain unverified.

## Handy shortcuts

These are the defaults. Open **Help → Keyboard shortcuts** to see your active assignments. To change them, open **Profiles → Global hotkeys**.

| Shortcut | Action |
|---|---|
| **F1** | Open Help |
| **Ctrl + Alt + F8** | Show or hide the outline |
| **Ctrl + Alt + F9** | Toggle click-through |
| **Ctrl + Alt + F10** | Apply the next profile |
| **Ctrl + Alt + 1–9** | Apply the profile assigned to that slot |
| **Esc** | Close Help or hide the outline while ExactFrame has focus; cancel an active drag |

If a shortcut is already in use by another app, ExactFrame shows that. It also avoids shortcuts that would interfere with typing characters using **AltGr** on your keyboard.

## Help and settings

Press **F1** or select **?** for searchable help and troubleshooting. For a bug report, open **Help → About → Copy system info**, then [open an issue](https://github.com/MaxAkbar/ExactFrame/issues). Include what you were trying to do and what happened; for recording problems, include your recorder, capture method and display scaling.

Settings, profiles and remembered app sizes are stored at:

```text
%LOCALAPPDATA%\ExactFrame\exactframe.db
```

Use **Help → About → Open settings folder** to find it.

## Build from source

On Windows, install the **.NET 10 SDK** specified in [global.json](global.json), then run:

```powershell
dotnet restore ExactFrame.slnx -p:Platform=x64 --locked-mode
dotnet run --project src/ExactFrame/ExactFrame.csproj -p:Platform=x64 --no-restore
```

For ARM64, use `-p:Platform=ARM64`. You can also open `ExactFrame.slnx` in Visual Studio with the **WinUI application development** workload.

See the [development guide](docs/development.md) for tests, portable builds, project layout and Windows checks.
