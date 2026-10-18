using static ExactFrame.Core.Help.HelpBlock;

namespace ExactFrame.Core.Help;

/// <summary>The in-app help. Keep it in step with README.md when features change.</summary>
public static class HelpContent
{
    public const string GettingStarted = "getting-started";
    public const string OutlineArea = "outline-area";
    public const string StyleAndGuides = "style";
    public const string Profiles = "profiles";
    public const string Shortcuts = "shortcuts";
    public const string ResizeWindow = "resize-window";
    public const string ObsIntegration = "obs";
    public const string RecordingSetup = "recording-setup";
    public const string Troubleshooting = "troubleshooting";
    public const string About = "about";

    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new(GettingStarted, "Getting started", "",
            "What ExactFrame does, and a quick way to set up your first recording.",
            [
            new("What ExactFrame does",
                [
                Paragraph("ExactFrame helps you record an exact part of your screen. It doesn't record anything itself: it shows " +
                              "you where to point your recorder, or resizes an app so it's exactly the size you'll record."),
                    Bullets(
                        "**Outline area** draws a border around the area you'll record. Point your recorder at that region.",
                        "**Resize window** makes another app an exact size, so you can record that app's window.")
                ]),
                new("Set up a recording in five steps",
                [
                    Steps(
                        "Pick a size, such as **1080p**, or drag the **Width** and **Height** sliders.",
                        "Choose where the frame sits with the anchor grid, and pick a display under the preview.",
                        "Select **Show outline**, or press Ctrl + Alt + F8.",
                        "Select **Copy bounds** and paste X, Y, width and height into your recorder's region settings.",
                        "Leave **Hide from recorders** on, or hide the outline before you start recording."),
                    Tip("All sizes are physical pixels, so 1920 × 1080 stays exactly 1920 × 1080 at 100%, 150% or 200% scaling."),
                    Paragraph("Use **Theme** in the top toolbar to choose **System**, **Light** or **Dark**. System follows Windows. The choice is remembered separately from capture profiles; Windows contrast themes take priority.")
                ])
            ]),

        new(OutlineArea, "Outline area", "",
            "Draw, move and resize the frame, or enter exact dimensions and coordinates.",
            [
                new("Size",
                [
                    Bullets(
                        "Pick a preset: **720p**, **1080p**, **1440p**, **4K** or **Vertical**. **Custom** lets you type any size.",
                        "Drag the **Width** and **Height** sliders. They go up to the largest size that fits the display, snap to " +
                        "even pixels and pull toward standard sizes, so 1920 × 1080 is easy to land on.",
                        "Type an exact number in the box next to each slider.",
                        "The chain button keeps the width-to-height ratio. Turn it off to change one side only.",
                        "The line under the sizes shows the logical size Windows apps see at the display's scaling.")
                ]),
                new("Position",
                [
                    Bullets(
                        "Use the 3 × 3 anchor grid to place the frame in a corner, on an edge or in the center.",
                        "Type **X** and **Y** for an exact position. In those boxes, arrow keys move 1 px and Page Up / Page Down 10 px.",
                        "Pick a display under the preview. Negative positions are normal on displays left of or above the main one.",
                        "**Keep clear of the taskbar** keeps the frame inside the area the taskbar doesn't cover.")
                ]),
                new("Draw and start from a window",
                [
                    Bullets(
                        "Select **Draw on screen**, then drag a rectangle on any connected display. It must be at least 100 × 100 physical pixels and fit inside one display's usable area.",
                        "Select **Use window bounds**, then click an existing window. ExactFrame uses its visible outer bounds and leaves the window's size alone.",
                        "Both selections turn the aspect lock off. Turn the chain button back on to keep the new ratio while resizing.",
                        "Right-click or Esc cancels either picker. Screen drawing also cancels after 30 seconds; window picking after 15 seconds.")
                ]),
                new("Additional frames",
                [
                    Bullets(
                        "Add a **9:16** guide for Shorts inside a 16:9 main frame, or add **1:1**, **4:5** and custom guides.",
                        "Name each composition and choose its output size. The source crop and output dimensions are separate: a smaller crop may need enlargement, which can soften text.",
                        "Drag a subframe directly in the large preview or drag its live border on screen. The selected subframe moves 1 px with arrows or 10 with Shift. Alt + drag moves the main frame underneath it; Esc cancels a drag.",
                        "Output width and height each have a slider and an exact pixel box. The small position map and **Crop X/Y** remain available for parent-relative adjustments.",
                        "Use an anchor or a free position. The free position stays relative as the main frame resizes. Adjust **Edge inset** for clearance and **Safe margin** for content guides.",
                        "**Preview outputs** shows each final composition with its scaling and safe margin. Different aspect ratios use empty bars instead of stretching.",
                        "**Refresh from screen** captures visible content while ExactFrame briefly hides. Images stay in memory until the dialog closes. Previews fit the window; actual recorder output remains unverified.",
                        "Move or resize the main frame and the extra guides follow it. Their interiors are click-through; turn on **Click-through** to lock their borders too.",
                        "**Copy bounds** copies a labeled region for every frame. Set up each region in your recorder or editor.",
                        "Save the setup as a profile to restore all its compositions together. Exported version 2 profiles retain output sizes, free positions and margins; older version 1 files still import as guides.")
                ]),
                new("On screen",
                [
                    Bullets(
                        "Drag the outline's border to move it. Drag an edge or corner handle to resize it. The frame stays inside the selected display's usable area.",
                        "In the preview, drag inside the frame to move it, use its handles to resize, or drag outside it to draw a new frame.",
                        "Focus the preview and use arrow keys to move 1 physical pixel, or Shift + arrows for 10. Ctrl + arrows resizes from the right and bottom edges; add Shift for 10 px.",
                        "**Snap to edges and centers** aligns pointer moves and handle resizes with the display and nearby windows. Keyboard adjustments bypass snapping.",
                        "Esc or right-click cancels an active drag. Otherwise Esc hides the outline while ExactFrame has focus.",
                        "Turn on **Click-through** so clicks pass through the border to the app underneath. The middle is always click-through.",
                        "Turn click-through off to edit with the pointer or preview keys. The preview is to scale and shows the frame's physical bounds."),
                    Tip("The colored border is drawn inside the frame, so its outer edges are exactly the area you record.")
                ])
            ]),

        new(StyleAndGuides, "Style and guides", "",
            "Change how the outline looks and add guides for composing your shot.",
            [
                new("Outline",
                [
                    Bullets(
                        "**Color**: teal, red, amber, blue or white. Pick one that stands out against what you're recording.",
                        "**Weight**: 2, 4 or 6 px.",
                        "**Line**: solid, dashed, or corners only for the least distraction.",
                        "**Size label** shows the frame size above its top-left corner, or inside when there's no room.")
                ]),
                new("Dimming and guides",
                [
                    Bullets(
                        "**Dim outside the frame** darkens everything you won't record. Set how dark with **Amount**.",
                        "**Thirds** draws rule-of-thirds lines for framing.",
                        "**Center mark** marks the middle of the frame.",
                        "**Safe area 90%** marks where text stays clear of the edges."),
                    Tip("Guides are drawn inside the frame. Keep **Hide from recorders** on so they stay out of the recording.")
                ]),
                new("On-screen controls",
                [
                    Paragraph("While the outline is showing, a small bar sits under it, or above it when there's no room. It shows the size " +
                              "and position, and has buttons to center the frame, toggle thirds guides, click-through and hiding from " +
                "recorders, open ExactFrame, and hide the outline."),
                    Bullets(
                        "After 3 seconds without the pointer nearby, it shrinks to just the size.",
                        "With click-through on, it only shows how to unlock.",
                        "It never takes focus from the app you're recording. Windows is asked to exclude it along with the outline; check a saved test clip for your capture method.",
                        "Turn it off in the **Style** tab with **On-screen controls**.")
                ])
            ]),

        new(Profiles, "Profiles", "",
            "Save setups you use often and switch between them with one shortcut.",
            [
                new("Save and use profiles",
                [
                    Steps(
                        "Set up the frame the way you want it.",
                        "Open the **Profiles** tab and select **Save current**.",
                        "Choose a new name. To replace a saved setup, use **Update from current** in its actions menu."),
                    Bullets(
                        "A profile remembers the size, position, display, mode, aspect lock and additional frames. In Resize window mode it also remembers the app. " +
                        "Check **Include appearance when saving or updating** to remember its outline style too. Other profiles use the default appearance.",
                        "Select a profile to apply it. The profile button in the title bar shows which one is active, and says " +
                        "**edited** once you change something.",
                        "Applying a Resize window profile resizes its app straight away, including from the profile's shortcut " +
                        "while another app has focus. ExactFrame uses a selected matching window or one unique match. " +
                        "If several windows match, select one under **Match this application** before resizing. If nothing matches, nothing is resized.",
                        "Numbered shortcuts stay assigned to the same profile when other profiles are deleted. New saves take the first free slot; " +
                        "duplicates and imports have none until you choose **Shortcut** in their actions menu. **Next profile** cycles through the list. " +
                        "To hold other keys with the number, select **Change** next to **Apply profile 1 to 9**.",
                        "Use a profile's actions menu to update, rename, duplicate, export, assign a shortcut or delete it.")
                ]),
                new("Transfer profiles between PCs",
                [
                    Steps("Select **Export all**, or **Export** in one profile's actions menu.",
                        "On the destination PC, select **Import** and choose the saved file.",
                        "Review the profiles and choose a connected display for every saved display, then select **Import copies**."),
                    Bullets("Imports add copies without replacing existing profiles, taking shortcuts or resizing an app. Select a profile when ready to apply it.",
                        "Sizes stay in physical pixels. Custom positions move relative to the chosen display and are kept inside it when they fit. " +
                        "A size that is too large stays unchanged and shows a warning. Missing source geometry centers a custom position.",
                        "Portable files include saved appearance and the target app's process name, but leave out shortcut assignments, OBS connections and other settings.")
                ]),
                new("Keep running in the background",
                [
                    Bullets(
                        "With **Keep running when closed** on, closing the window leaves ExactFrame in the notification area, " +
                        "so shortcuts and the outline keep working.",
                        "Select the ExactFrame icon in the notification area to open it. Right-click the icon to show or hide " +
                        "the outline, apply a profile, or quit.",
                        "**Start with Windows** starts ExactFrame in the notification area when you sign in.",
                        "Only one copy runs at a time. Starting ExactFrame again opens the copy that's already running.")
                ])
            ]),

        new(Shortcuts, "Keyboard shortcuts", "",
            "Global shortcuts work while another app has focus.",
            [
                new("Change a shortcut",
                [
                    Steps(
                        "Open the **Profiles** tab.",
                        "Under **Global hotkeys**, select **Change** next to the shortcut.",
                        "Press the new keys and select **Use shortcut**."),
                    Bullets(
                        "A shortcut needs Ctrl, Alt or Win, so ordinary typing never triggers it.",
                        "**Taken by another app** means Windows gave that shortcut to another app. Choose different keys.",
                        "Windows treats AltGr as Ctrl + Alt, so on some keyboards a Ctrl + Alt shortcut is a character, such " +
                        "as AltGr + 2 for @ on a Spanish keyboard. ExactFrame doesn't use a shortcut that types a character on " +
                        "one of your keyboard layouts, so you can still type it. Those show **Types … on your keyboard**.")
                ])
            ],
            HelpTopicExtra.Shortcuts),

        new(ResizeWindow, "Resize window", "",
            "Make another app an exact size so you can record its window.",
            [
                new("Resize an app",
                [
                    Steps(
                "Select **Resize window** at the top of ExactFrame.",
                        "Choose the app in the list, or select **Pick on screen** and click it. Right-click or Esc cancels.",
                        "Set the size with a preset, the sliders or the boxes.",
                        "Under **Size applies to**, choose **Client area** for the app without its title bar and borders, **Whole window** " +
                        "for the visible frame, or **Web page** for a Chrome or Edge page without its tabs and toolbar.",
                        "Under **Placement**, choose **Center** or **Keep position**.",
                        "Select **Resize window**.")
                ]),
                new("What happens",
                [
                    Bullets(
                        "A minimized or maximized app is first restored to a normal window.",
                        "The app is brought to the front, moved to the display, measured, adjusted and checked.",
                        "After a manual resize, ExactFrame remembers the measured size and measurement choice for that app.",
                        "**Match this application** adds optional exact executable path, Contains or Exact window title, and exact window class conditions. All conditions must match, ignoring letter case. Use selected window details to fill them, or enter the app's process name to preview matches without selecting an app first.",
                        "Selecting a match in that section only previews its title, path, class, position and size. Choose **Resize window** to apply, **Save setup as profile** to save, or **Update active resize profile** to replace the active resize setup after confirmation.",
                        "Remembered sizes can differ between windows of one app. More specific conditions take priority over process-only sizes. If equally specific rules conflict, no remembered size is applied. **Forget size** removes only the matched rule.",
                        "Client area leaves out the title bar even when the app draws its own, like the tab strip in Chrome and Edge or the title bar in VS Code. In a browser it includes the toolbar; choose Web page to size only the page.",
                        "Web page mode prefers the rendered document's Windows accessibility bounds, with a Chromium page HWND or matching page element as fallback. If no page can be measured, ExactFrame shows an error before moving the app. Restore a minimized browser first.",
                        "Selecting that app again applies its remembered size automatically. It keeps the current position if it fits, otherwise centers the app.",
                        "If the remembered size cannot fit on the app's display, ExactFrame leaves the app alone and shows a warning. Select Forget size to remove the saved size.",
                        "The outline marks the app's actual size and position, then follows it when it moves or changes size.",
                        "Move the app itself; the outline's border is click-through while following. It also follows the app across displays.",
                        "If the app is minimized or hidden, the outline disappears and returns when the app is visible again.",
                        "Hiding the outline, choosing another app, changing the requested frame or restoring the original size stops following.",
                        "If the app won't take that exact size, the status bar shows what you asked for and what it accepted. Chromium may round a web page by a pixel or two at some display scales.")
                ]),
                new("Undo a resize",
                [
                    Paragraph("**Restore original size** puts the app back exactly as it was before the first resize, including " +
                "minimized or maximized. ExactFrame remembers this until you close it."),
                    Tip("A 1920 × 1080 client area or web page plus browser chrome won't fit wholly on a 1920 × 1080 display. " +
                        "Use **Whole window**, a smaller size, or the app's own full-screen mode.")
                ]),
                new("Browser sizing: physical pixels and CSS",
                [
                    Bullets(
                        "All ExactFrame width and height controls use physical screen pixels. **Web page** measures a page capture area through Windows; it does not read the page's CSS viewport.",
                        "The Windows logical-size note is a rounded conversion using display scaling. It is not a CSS viewport measurement or a browser zoom reading.",
                        "CSS viewport dimensions, browser page zoom and device-pixel ratio (DPR) are not measured by ExactFrame. DPR is a browser-reported ratio and can change with page zoom or display changes; Windows scaling alone does not establish it."),
                    Steps(
                        "Set the intended page zoom in the browser menu and note the browser version. Turn off device emulation for a normal desktop check, or record the emulation settings separately.",
                        "In the browser's DevTools Console, read **window.innerWidth** and **window.innerHeight** for CSS layout viewport dimensions, and **window.devicePixelRatio** for DPR. Keep these values separate from ExactFrame's physical dimensions.",
                        "Record the browser menu's zoom value separately. **window.visualViewport.scale**, when available, describes pinch scaling rather than the browser menu's page zoom.",
                        "Recheck after changing zoom, display, browser panels or emulation. A recorded video's dimensions still need their own recorder check."),
                    Tip("CSS viewport dimensions multiplied by DPR give an estimate for a normal desktop page. Scrollbars, rounding, pinch scaling and emulation can affect comparisons; the estimate is not a native capture measurement.")
                ])
            ]),

        new(ObsIntegration, "OBS Studio", "\uE714",
            "Preview and apply an ExactFrame region to an OBS Display Capture scene item.",
            [
                new("Connect on this PC", [Steps(
                    "In OBS Studio 28 or later, open **Tools > WebSocket Server Settings** and enable the WebSocket server. Keep authentication enabled.",
                    "In ExactFrame, select **Send to OBS**. Enter the server port (normally 4455) and password from OBS, then select **Connect**.",
                    "The password is cleared after connecting and is never saved. The connection and its undo history stay in memory until disconnected or ExactFrame exits.")]),
                new("Preview and apply", [Steps(
                    "Choose the main frame or an additional guide, the OBS scene, and a **Display Capture** placed directly in that scene. It must be visible, unlocked and unfiltered.",
                    "Choose **Fit to canvas** to scale uniformly, or **Actual pixels** to center without scaling. The frame must fit on the captured display; Actual pixels must also fit on the canvas.",
                    "Select **Preview change**. The drawing shows proposed geometry, rather than a captured image. Review the crop, scale, canvas and output dimensions.",
                    "Stop recording or streaming, then select **Apply to OBS**. ExactFrame rechecks the source and verifies its resulting transform. Check OBS's preview before recording.")]),
                new("Undo and limitations", [Bullets(
                    "**Undo last apply** restores the most recent item's previous crop and placement while the same connection stays open. It refuses to overwrite later edits made in OBS.",
                    "ExactFrame changes only the selected scene item. It does not change OBS video settings, switch scenes, create sources, start recording, or follow future frame movements automatically.",
                    "Display Capture sources are matched to Windows monitor identities, including displays with negative coordinates and different display scaling. Unknown displays are rejected instead of guessed.",
                    "Window Capture, grouped sources and other source types are not supported in this first integration. Use an unfiltered Display Capture source.")])
            ]),
        new(RecordingSetup, "Check my setup", "\uE73E",
            "Verify a saved test recording for your recorder, capture method, app version and display scaling.",
            [
                new("A short recording check", [Steps(
                    "Select **Check my setup** in the title bar. Note your recorder version and exact capture method; **Automatic** may choose a different method on another display.",
                    "Record a 5–10 second test clip with a test page or pattern inside the frame. Keep the outline, guides and floating control bar visible, with **Hide from recorders** on. Include dimming and the magnifier if you use them.",
                    "Stop recording and open the saved video. Confirm the file dimensions, all four edges and corner markers, and that every overlay is absent. Account for the recorder's canvas, output scaling and any encoder rescale setting.",
                    "Reopen **Check my setup**, enter the clip reference and mark only what you observed. **Copy setup report** includes Windows, monitor geometry, scaling and the current frame. Results are user-reported, not automatically verified.")]),
                new("Repeat when your setup changes", [Bullets(
                    "Repeat after changing the recorder, capture method, browser version or zoom, display scaling, or monitor. Record monitor transitions separately before relying on them.",
                    "**Windows accepted capture exclusion** means the native API succeeded. It does not prove that your capture method honors the request. If Windows rejects the floating control bar request, ExactFrame reports the failure and retries instead of remembering it as applied.",
                    "If any overlay appears in the saved file, hide the outline and floating controls before recording. The main ExactFrame window is never excluded; move it outside the capture region or minimize it.")])
            ]),
        new(Troubleshooting, "Troubleshooting", "",
            "Fixes for the most common problems.",
            [
                new("The outline shows up in my recording",
                [
                    Paragraph("**Hide from recorders** asks Windows to leave the outline out of captures, but not every recorder or " +
                              "capture method honors it. Check your recorder's preview. If you can see the outline there, hide it " +
                              "with Ctrl + Alt + F8 before recording.")
                ]),
                new("A shortcut says it's taken by another app",
                [
                    Paragraph("Another app registered those keys first. Select **Change** next to it in the **Profiles** tab and pick different keys.")
                ]),
                new("A shortcut says it types a character on my keyboard",
                [
                    Paragraph("On your keyboard, AltGr with that key types a character, and Windows sends AltGr as Ctrl + Alt. " +
                              "ExactFrame leaves the shortcut unused so the character still types. Select **Change** next to it " +
                              "in the **Profiles** tab and choose keys with Shift or Win, such as Ctrl + Shift + 1.")
                ]),
                new("Shortcuts stop working after I close the window",
                [
                    Paragraph("Turn on **Keep running when closed** in the **Profiles** tab. Closing the window then leaves " +
                              "ExactFrame in the notification area, and its shortcuts keep working until you quit it from the icon.")
                ]),
                new("An app won't resize, or ends up a different size",
                [
                    Bullets(
                        "Some apps have a minimum or fixed size. The status bar shows the size the app accepted.",
                "If the app runs as administrator, run ExactFrame as administrator too.",
                        "Games in exclusive full-screen mode can't be resized.",
                        "If the app closed or changed, refresh the window list and select it again.")
                ]),
                new("The frame doesn't fit",
                [
                Paragraph("ExactFrame never shrinks a frame to make it fit. Choose a smaller size, another display, or turn off " +
                              "**Keep clear of the taskbar**.")
                ]),
                new("The size looks wrong in my recorder",
                [
                Paragraph("ExactFrame uses physical pixels. Paste the values from **Copy bounds** into your recorder's region " +
                              "settings, and check the recorder isn't scaling its output.")
                ]),
                new("Reset settings",
                [
                Paragraph("Close ExactFrame, then rename the **ExactFrame** folder under your local app data directory " +
                "to keep a backup. The next launch creates a fresh database with default settings. " +
                "The **About** page shows the database path. Renaming the whole folder also prevents an old " +
                "settings.json from being imported again.")
                ])
            ]),

        new(About, "About", "",
            "Version details, where settings are kept, and credits.",
            [
                new("Credits",
                [
                    Paragraph("Text uses IBM Plex Sans and IBM Plex Mono, licensed under the SIL Open Font License 1.1.")
                ])
            ],
            HelpTopicExtra.About)
    ];

    /// <summary>Fixed shortcuts, shown after the changeable global hotkeys.</summary>
    public static IReadOnlyList<ShortcutRow> FixedShortcuts { get; } =
    [
        new("Cancel a drag; otherwise hide the outline while ExactFrame has focus", ["Esc"]),
        new("Open help", ["F1"]),
        new("Move the frame in the focused preview by 1 px", ["↑ ↓ ← →"]),
        new("Move the frame in the focused preview by 10 px", ["Shift", "↑ ↓ ← →"]),
        new("Resize in the focused preview by 1 px; add Shift for 10 px", ["Ctrl", "↑ ↓ ← →"]),
        new("Nudge X or Y by 1 px, or 10 px", ["↑ ↓", "Page Up / Down"]),
        new("Move a size slider by 2 px, or 64 px", ["← →", "Page Up / Down"])
    ];

    public static HelpTopic Find(string id) => Topics.FirstOrDefault(t => t.Id == id) ?? Topics[0];
}
