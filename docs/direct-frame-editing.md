# Direct frame editing

These controls are available in **Outline area** in the current source build. The published v2.0.0 release predates this work.

## Set up a frame

- Select **Draw on screen** and drag a rectangle on a connected display. Release to accept; Esc or right-click cancels. Selection expires after 30 seconds. A selection must be at least 100 × 100 physical pixels, no more than 16,384 px per side, and fit inside one display's usable area. **Keep clear of the taskbar** also applies.
- Select **Use window bounds**, then click a window within 15 seconds. This copies its visible outer bounds without resizing that application. Esc or right-click cancels. A window crossing displays, overlapping the taskbar clearance, or outside the size limits is rejected with an explanation. Adjust the window or draw a frame in that case.
- Both freeform selections turn the aspect lock off. Turn the chain button back on to keep the new ratio while adjusting handles. Presets, numeric dimensions, X/Y and anchors remain available.

Screen drawing uses temporary dimmed input surfaces. ExactFrame requests capture exclusion before showing any selector; if Windows rejects it, selection stops with an error. This API result does not prove that a recorder honors exclusion. Follow [the recording checks](recording-compatibility.md) for your recorder.

## Move and resize

The preview is an editor: drag inside the frame to move it; drag one of eight edge/corner handles to resize it. Start a drag outside the frame to draw a replacement. The visible outline also has eight handles; drag the rest of its border to move it. Its interior remains click-through.

| While the preview has focus | Result |
| --- | --- |
| Arrow | Move by 1 physical pixel |
| Shift + arrow | Move by 10 physical pixels |
| Ctrl + arrow | Resize from the right/bottom edges by 1 physical pixel |
| Ctrl + Shift + arrow | Resize from the right/bottom edges by 10 physical pixels |
| Esc during a drag | Restore the frame from before that drag |

With aspect lock on, resizing adjusts the other dimension to maintain the ratio, with integer-pixel rounding. Size limits and display boundaries can reduce or prevent a requested adjustment. These preview shortcuts apply only while the preview has focus. The numeric boxes and sliders keep their existing shortcuts.

**Snap to edges and centers** applies to pointer moves and handle resizes. It uses the display's usable edges/center and up to 32 visible nearby window bounds, refreshed at the start of each gesture. Drawing a new frame and keyboard adjustments do not snap. Turn snapping off for unrestricted pointer adjustments within the display.

Right-click or Esc cancels a pointer drag. Losing capture or focus, changing the display, or switching modes cancels the affected gesture. A new preset or numeric edit takes precedence over an interrupted drag. Turn click-through off to use the handles or preview keyboard editing. Frames following a resized application remain locked to its measured bounds.

## Windows interaction checks

The automated Core suite covers physical-coordinate geometry, snapping, aspect ratios, limits and state restoration with substitute services. Native interaction checks also ran on 2026-10-18 after the desktop became available; the results and remaining limits appear below. A successful build, correct UI measurement or accepted capture-exclusion request does not establish recorded output.

Run on an unlocked Windows desktop with the current source build. Record the commit or source diff, Windows build, display bounds/scaling and result for each case. Use a disposable target window when selecting bounds.

| Case | Procedure and expected result |
| --- | --- |
| Preview move and resize | Show a 1280 × 720 frame. Move its interior, then resize all eight handles. Dimensions, X/Y, preset label, copied bounds and live outline agree after every gesture. The opposite edge/corner stays fixed; single-edge aspect resizing keeps the perpendicular center within rounding. |
| Preview draw | Drag outside the old frame in all four directions. The new frame matches the accepted rectangle and aspect lock turns off. A rectangle smaller than 100 px on either axis restores the old frame. |
| Screen drawing | Draw on each monitor, including one with a negative origin. Check accepted physical coordinates and taskbar clearance. Try Esc, right-click, an undersized selection and the 30-second timeout. The old frame and its prior visibility return on cancellation. |
| Window bounds | Pick a normal disposable window on each display. Compare the copied bounds with its visible outer bounds and confirm the application was not moved or resized. Cancel, then try a window across monitors and one overlapping taskbar clearance. No remembered resize is applied. |
| Keyboard and focus | Tab to or click the preview. With aspect lock off, compare X/Y after each arrow (1 px) and Shift + arrow (10 px), then dimensions after Ctrl and Ctrl + Shift. Enable aspect lock and verify the ratio within integer rounding. Typing in numeric fields and other applications must retain its normal behavior. |
| Native handles | Move the visible border and resize all eight handles, with aspect lock both on and off. Release at a new position, including a quick move/release. Check the final copied bounds. Clicks in the interior reach the underlying application. |
| Snapping | Place a disposable window near the frame. Move and resize toward display/window edges and centers. Continue past the snap point to check smooth release. Turn snapping off and verify the same drag is free; arrow adjustments remain exact near snap targets. |
| Cancellation and newer actions | Esc/right-click during preview and native drags restores prior dimensions, position, preset and aspect lock. Change a preset (including one with the same geometry), numeric value, display, taskbar option or mode during an interrupted gesture; the newer action wins. Minimize/restore or resize the ExactFrame window during a preview gesture. No stuck capture, hidden selector or leftover keyboard hook remains. |
| Scaling and transitions | Repeat at 100%, 150%, 175% and 200% where available, including mixed scaling and negative origins. Draw on the other monitor, switch selected display, and change the display configuration during selection. Verify the accepted physical bounds and cleanup. Editing stays within one display. |
| Read-only states and guides | Enable click-through and switch to Resize window mode. Preview input and native handles cannot change measured bounds. Add nested guides, then edit the main frame; copied guide bounds and preview remain inside it. |
| Recorder output | Recheck outline/handle/control-bar capture with each recorder method you use. API acceptance and correct on-screen bounds are separate from recorded output. Retain clips and update the compatibility matrix only with actual evidence. |

The [Windows recording harness](../tests/ExactFrame.WindowsChecks/README.md) tests measurements and recorder output separately; it does not automate this pointer/keyboard checklist.

## Validation on 2026-10-18

Environment: Windows build 26300.9711, x64; two 3840 × 2160 displays, with 150% scaling on the left display at a negative origin and 175% on the primary display. The ExactFrame application window remained on the 175% display. This is a bounded interaction run on one machine.

The first build exposed a fast-drag defect: reading the current pointer at button-down could read the already-injected release position, collapsing a native drag to zero size. Native drag starts now use the signed coordinates of the queued button-down event, converted to screen coordinates. Later updates still read the current pointer because the border itself moves during a gesture. See Microsoft's [WM_LBUTTONDOWN](https://learn.microsoft.com/windows/win32/inputdev/wm-lbuttondown) and [ClientToScreen](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-clienttoscreen) documentation. Selection now ignores unrelated settings broadcasts, and new selections/presets clear old geometry announcements.

| Exercised case | Observed result |
| --- | --- |
| Preview editing, initial build | Moved a frame and resized all eight handles with snapping off, using the selected negative-origin 150% display. Opposite edges stayed fixed. A freeform replacement was accepted; an undersized drawing restored the old frame. |
| Preview keys, initial build | Right moved X from -2560 to -2559; Shift + Down moved Y from 720 to 730. Locked Ctrl + Right changed 1280 × 720 to 1281 × 721, then Ctrl + Shift + Right to 1291 × 726. |
| Existing-window bounds, initial build | Selected a blank Notepad tab's window. ExactFrame adopted its 2721 × 1949 physical bounds at 95, 126 and selected the 175% display. Its window capture metadata stayed at the same origin and 1555 × 1114 logical dimensions. The test tab was closed without editing existing notes. |
| Screen drawing, corrected build | A 320 × 280 logical-pixel drag on the primary display produced a 560 × 490 physical frame at 949, 670, selected that display and showed the outline. This same fast input previously canceled with zero size. |
| Live border and handles, corrected build | All eight native resize handles changed their expected edges. Border movement changed 932, 652 to 967, 687 while retaining 630 × 560. No other application's size was changed by these actions. |
| Native aspect lock, corrected build | A right-side resize produced 1297 × 730 at 1280, 715, retaining the left edge and vertical center. A subsequent corner resize produced 1330 × 748 at the same top-left, preserving 16:9 within integer rounding. |
| Snapping and keys, corrected build | Preview movement snapped a 630 × 560 frame to the display center at 1605, 800. Right then moved X to 1606 despite snapping; Ctrl + Shift + Right changed width to 640. A left-arrow in the width text field did not move the frame. |
| Screen-picker cancellation, corrected build | Undersized selection, Escape, right-click and the 30-second timeout restored the prior frame and visible outline. Timeout cleanup was observed after 32.9 seconds. |
| Status, corrected build | Selecting a preset cleared the prior geometry announcement. Frame bounds, dimensions, preset label and the bounds shown next to Copy bounds updated together during the exercised edits. |
| Automated checks | 271 Core tests passed; the app and Windows recording harness built with zero warnings and errors. A separate comparison of 22 saved UI measurement observations matched the expected results. |

Local evidence is retained under `artifacts/frame-editing-checks/20261018-native-checks/`: separate initial/corrected UI observations, compiled-binary hashes, source snapshot/manifest, tracked diff and measurement comparisons. Personal notes, desktop images and recorder clips are not included. These artifacts are local and are not part of the published compatibility matrix.

Still pending: native handles and screen drawing on the negative-origin 150% display; moving the ExactFrame window between DPI contexts; 100%/200% scaling; display disconnects or work-area changes during selection; Escape/right-click while a border drag is held; all snapping approach/release combinations; and native click-through/followed-window/nested-guide combinations. Core tests cover several of these state transitions, but substitute services do not establish native behavior. Recorder exclusion and output verification remain separate requirements in the [compatibility matrix](recording-compatibility.md).
