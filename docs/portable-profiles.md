# Portable profiles

Available in ExactFrame 2.1.0 and later.

## Save and manage a setup

In **Profiles**, **Save current** creates a new setup. It stores the physical size, position or anchor, display, taskbar clearance, mode, aspect lock and additional frames. Resize profiles also store the measurement area and app process name. An existing name is rejected; saving never silently overwrites another profile.

Check **Include appearance when saving or updating** to store the outline color, thickness, line, dimming, guides, magnifier and floating controls with the profile. Applying it restores that appearance without replacing the default appearance. Profiles without saved appearance use the default. Edits to saved appearance take effect immediately; choose **Update from current** to keep them in the profile.

Each profile has a **More actions** menu:

| Action | Result |
| --- | --- |
| Update from current | Confirms replacement with the current setup; keeps the profile's name, identity and shortcut. |
| Rename | Changes only the name. |
| Duplicate | Creates a separately named copy, including additional frames and saved appearance. It starts without a numbered shortcut and is not applied. |
| Export | Saves that profile to a portable file. |
| Shortcut | Assigns a number from 1 to 9 or removes the assignment. Taking an occupied number requires confirmation. |
| Delete | Confirms removal of the saved profile. Other profiles keep their shortcut numbers. |

New saves receive the first free numbered shortcut, if one is available. Existing installations keep the former first-nine assignments during a one-time migration. Deleting or reordering profiles does not move those assignments. Imported profiles and duplicates have no numbered shortcut until assigned. **Change** beside **Apply profile 1 to 9** changes the keys held with the number. Registration conflicts and shortcuts that type characters retain their existing warnings.

Applying a Resize profile can resize its matching app immediately. A selected matching window wins; otherwise exactly one window must match. With several matches and no selected match, choose one under **Match this application**, then resize. Importing, duplicating, renaming, updating and exporting do not resize an app. A profile tied to a disconnected display cannot be applied; export it and import a copy to choose a connected display.

## Transfer a setup

1. Select **Export all**, or **Export** in one profile's actions menu. Save the `.exactframe.json` file.
2. On the destination PC, select **Import** and open the file.
3. Review the names and choose a connected display for each saved display. Display numbers and device names are local hints and can differ between PCs.
4. Select **Import copies**. Existing profiles remain in place; colliding names receive a numbered suffix. Nothing is applied automatically. Select the imported profile when ready.

Physical pixel sizes and anchors are preserved. A custom position is translated relative to the source display's usable origin onto the chosen display. If the frame fits, its position is clamped inside the destination's usable area. Taskbar clearance uses the saved setting. Different DPI scaling does not scale the pixel dimensions. A frame too large for the destination keeps its size and produces a warning. Missing source geometry centers a custom position and produces a warning. A display disconnecting during review stops the import before profiles are added.

Files contain profiles, optional saved appearance and source display geometry. They omit numbered shortcut assignments, native monitor interface paths, OBS addresses/passwords, startup settings and unrelated app data. Profile names, target process names and optional executable paths, title text and window classes are readable in the file; review these when sharing it. Matching conditions are retained across PCs, so a different install path may require editing under **Match this application**.

Exports with window matching conditions use version 3, which requires a reader that understands those conditions. Other exports remain version 2, preserving output composition sizes, free positions and margins. Versions 1 and 2 remain importable with process-only targeting and their original guide defaults. All supported versions accept at most 100 profiles, 16 additional frames per profile, and 1 MiB per file. Invalid, unsupported, incomplete or conflicting documents are rejected as a whole. Conditions in a version 1 or 2 file are rejected instead of silently broadened. The file is validated before review and remapping; no partial import is retained if saving fails. Exporting more than 100 saved profiles at once requires smaller individual exports.

## Verification

The Core suite checks package validation, remapping, migration, profile identity, stable shortcut behavior, optional appearance, cancellation and persistence. These checks use substitute display, window and dialog services. Native Windows picker and dialog checks are recorded separately below; Core tests alone do not prove native picker behavior or another PC's display arrangement.

### Results on 2026-10-18

- **373 Core tests passed**, with no failures or skips. The suite includes 64 portable-package cases, workflow and shortcut regressions, and real JSON/database reopen checks for the new fields.
- The Release x64 app and Windows integration-check harness built with **zero warnings and errors**. The harness was compiled; unrelated recording checks were not rerun for this change.
- Native checks used isolated settings on the existing Windows desktop, with two 3840 × 2160 displays at 150% and 175% scaling, including a negative-origin display. The Profiles panel, appearance checkbox and action menus rendered within the existing sidebar.
- **Duplicate** created an unassigned copy that remained after an app restart. **Rename** changed that copy's name. **Shortcut** listed existing owners and free slots; assigning slot 4 displayed Ctrl + Alt + 4, and pressing that shortcut applied the intended copy.
- Native export initially rejected the compound extension in the picker configuration. The picker now uses `.json` and suggests a filename ending in `.exactframe`. The corrected **Export all** opened Windows Save As with `ExactFrame profiles.exactframe.json` and a JSON filter.
- **Native export completion, native import/review and cross-PC round trips remain unverified.** The automation inventory did not expose the separate Windows `PickerHost` Save As window as a target, and rejected input from the app's owner window. No test file was written through that picker. Package round trips and import review decisions were exercised with substitute services.
- Original settings were restored after the isolated checks and all three original files matched their saved SHA-256 hashes before reopening the app. Local receipts and disposable test data are under ignored `artifacts/profile-checks/20261018-native/`.

Before a release, repeat the following on an unlocked Windows desktop:

- Export one profile and all profiles; cancel each picker once. Open the resulting files and import them with a chosen destination display.
- Confirm the review lists the profiles and display mappings. Cancel review, then import; existing profiles and active frame must stay unchanged.
- Use Rename, Duplicate and Update from current. Restart and check names, saved appearance, frame geometry and shortcuts.
- Remove an earlier test profile and confirm later numbered shortcuts still apply their original profiles. Reassign an occupied number and cancel once before confirming.
- Import onto a negative-origin display and a smaller display at different scaling. Confirm preserved physical dimensions, translated position and oversized warnings.

Cross-PC and recorder-output validation remain separate from these profile checks.
