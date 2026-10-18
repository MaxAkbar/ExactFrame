# Output compositions

Available in ExactFrame 2.1.0 and later.

## Source crops and final output sizes

Add a 9:16, 1:1, 4:5 or custom frame under **Additional frames**. Each composition has a name, source crop and optional final output size. Shorts, square, portrait and landscape output presets also set the crop ratio; changing the output dimensions afterward keeps the crop you chose. **Guide only** leaves the output size unspecified.

The source crop always describes physical screen pixels. For example, a 1920 × 1080 main frame with the default 8 px inset contains a 594 × 1056 exact 9:16 crop. Requesting 1080 × 1920 output requires 1.82× enlargement. It does not create a native 1080 × 1920 source region, and enlarged text may look softer. The editor and output preview report this relationship. If source and output aspect ratios differ, the preview fits the crop uniformly with empty bars rather than stretching it.

These are composition plans. They do not set an encoder resolution, change an OBS canvas or produce a video. **Copy bounds** and **Send to OBS** continue to use the source crop; configure output size and scaling in the recorder or editor.

## Position and safe margins

- Drag a subframe inside the large preview to move it independently. It gets a selection outline; arrow keys move that selected crop 1 physical pixel, or 10 with Shift. Hold Alt while dragging to move the main frame underneath a subframe. Main-frame handles keep their resize behavior. Overlapping crop borders are hit before interiors; otherwise the last-added overlapping crop is picked.
- With the outline shown and **Click-through** off, drag an additional frame's border directly on screen. Its interior still passes clicks through to the application. Turning click-through on locks all borders. Moves stay inside the parent inset and preserve source crop dimensions and requested output size.
- The small position map and **Crop X/Y** remain available for parent-relative movement. Esc, right-click or lost capture cancels an active drag. A newer numeric or preset edit takes precedence over a canceled older drag.
- Output width and height have sliders and exact pixel boxes, like the main size editor. Sliders normally reach 4096 px; entering a larger supported value extends the range without reducing it. Changing output dimensions does not resize the source crop.
- An anchor places the crop on a 3 × 3 grid. A free position keeps its proportion of the available horizontal and vertical travel when the parent resizes. Moving the parent moves every composition by the same amount.
- **Edge inset** sets clearance from the main frame, from 0 to 1000 physical pixels. A large inset can leave no usable crop; the UI reports that it does not fit.
- **Safe margin** draws an inset content guide on each side of the crop, from 0% (off) to 40%. It appears in the main preview, live overlay and output preview. This is a user-defined composition guide, not a guarantee about any platform's controls or captions.

## Preview content

**Preview outputs** shows a separate final-composition card for every additional frame, with source dimensions, output dimensions, scaling, optional bars and safe margins. Previews fit the dialog; they are not a 1:1 test of final text sharpness.

Choose **Refresh from screen** to capture the visible main recording frame once. ExactFrame briefly hides its main window while taking the snapshot. Pixels are held in memory and discarded when the dialog closes; no image file is written. Refresh after moving content or changing applications. Other visible windows affect the snapshot, and this screen capture does not verify what a recorder will encode. A screen preview requires the main frame to fit on one connected display and contain at most 16,777,216 pixels.

Profiles, duplicates, last-session settings and exports preserve the composition fields. Profile package version 2 adds output sizes, free positions, edge insets and safe margins. Version 1 packages remain importable with their original anchored placement, 8 px inset, no content-safe guide and no assumed output size. Version 2 packages require a reader that understands version 2. There are at most 16 additional frames per setup.

## Verification

The Core tests cover exact-ratio geometry, free positioning on negative origins, clamping, parent movement and resizing, output enlargement, uniform fitting with bars, tiny safe areas, old profile defaults, versioned packages and both real settings stores. The preview command freezes its setup while the dialog is open and does not resize an application.

### Results on 2026-10-18

- 390 Core tests passed with no failures or skips. The Release x64 app and Windows-check harness built without warnings or errors; unrelated recording harness scenarios were not rerun.
- Native checks ran on the existing desktop with 3840 × 2160 displays at 150% and 175% scaling. The 175% display was primary for these checks.
- A temporary 9:16 composition in a 2385 × 1342 main frame resolved to 738 × 1312 source pixels and reported 1.46× enlargement to 1080 × 1920. Its output card and safe-margin guide rendered correctly.
- **Refresh from screen** briefly hid ExactFrame, restored it and displayed actual visible screen content cropped to the composition. The snapshot remained in memory and was discarded when the dialog closed.
- Dragging the crop changed parent-relative X from 823 to 184 without changing its 738 × 1312 size. Right moved it to 185; Shift + Right moved it to 195. The main frame dimensions stayed unchanged.
- UI review caught an initial binding echo that switched an anchored guide to free positioning. The setter now ignores writes of its current pixel position, and a regression test covers this. The preview dialog was also narrowed to fit its content within the standard dialog width.
- Test compositions were not saved. Reopening the app restored the user's prior main-frame settings with no additional frames.

Recorder output, cross-PC behavior, native cancellation during an active pointer drag and mixed-DPI monitor transitions remain separate checks; a successful screen snapshot is not a recording compatibility result. Local check receipts are under ignored `artifacts/composition-checks/20261018-native/`. No captured screen images are retained there.

### Subframe controls follow-up on 2026-10-18

- 399 Core tests passed with no failures or skips. Release x64 builds of the app and Windows-check harness completed with no warnings or errors. The harness was built, not rerun against recorders.
- On the existing 150% display with a negative screen origin, dragging a subframe in the large preview moved its parent-relative X from 823 to 1092. Right moved it to 1093, and Shift + Right to 1103. The 2385 × 1342 main frame, 738 × 1312 source crop and requested 1080 × 1920 output stayed unchanged.
- Dragging the output width slider changed both the slider and number box to 2702; the height slider changed both to 2935. Entering 10000 for width extended the slider range and retained 10000 after committing the box. The source crop stayed unchanged.
- Native review exposed a two-way position-selection binding echo that reset a move before bounds refreshed. Reapplying the current position selection now does nothing. The source-position label also now receives the correct property notification. A regression test covers both, and a final native drag confirmed that the label and numeric position agree.
- Showing the live outline reported that Windows accepted capture exclusion. The desktop helper did not expose the overlay tool windows as input targets, so direct live-border dragging remains a manual check. Core tests cover the overlay event path, clamping, cancellation, external edits and click-through interruption.
- Runtime test edits were discarded; reopening the final app restored the user's saved main frame and existing Shorts composition. Local receipts are under ignored `artifacts/subframe-controls/20261018-native/`, without screen images.
