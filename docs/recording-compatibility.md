# Recording compatibility and evidence

ExactFrame measures a desktop rectangle. A successful resize or capture-exclusion API call does not verify a video file. The released 2.0.0 download predates the setup workflow and control bar fix described here.

## Status definitions

| Status | What it establishes |
| --- | --- |
| Core tests | Geometry and state behavior with substitutes; no native compatibility claim. |
| Native checked | The production Windows implementation ran against the named fixture/app and display. No recording claim. |
| Recorded pattern checked | Saved clips were decoded and checked for dimensions, corner markers and overlay differences, with a visible positive control. Limited to the exact row and sampled frames. |
| User-reported | A person marked the setup checklist after inspecting their clip. ExactFrame has not inspected it. |
| Not tested / incomplete | No usable evidence for this combination. This never means supported or passed. |

The app says **Windows accepted capture exclusion** only for the API request. A control bar failure is reported with its Windows error code and retried. **Recorded result: unverified** remains the initial setup-check status.

## Compatibility matrix

Evidence date: 2026-10-18. Windows build 26300, x64; source changes after checkpoint `16f5f47`. Source fingerprints and raw attempts are retained with each local run. Results below are filled only from completed checks.

| Recorder / method | App / browser version | Display scale | Monitor movement | Native measurement | Saved recording | Scope / evidence |
| --- | --- | --- | --- | --- | --- | --- |
| OBS Studio 32.2.2 / Display Capture / DXGI | Native WinUI pattern | 150%, negative X origin | Stationary | API rectangles only | Inconclusive | Three 1280×800 clips encoded, but black output; corner markers and visible positive control failed. No exclusion pass. |
| OBS Studio 32.2.2 / Display Capture / Windows Graphics Capture | Native WinUI pattern | 150%, negative X origin | Stationary | API rectangles only | Inconclusive | Same failure in three separate clips. Equality with a black baseline is not evidence of exclusion. |
| None | Native WinUI pattern | 150% → 175% | Resize onto both monitors | API rectangles matched | Not tested | Client and whole-window requests matched independent Win32/DWM reads. Visible behavior was not checked; rerun unlocked. |
| None | Chrome 155.0.8059.80 | 150% / 175% | Not tested | Not tested | Not tested | Binary version observed; does not establish viewport or capture support. |
| None | Edge 155.0.4283.45 | 150% / 175% | Not tested | Not tested | Not tested | Binary version observed; does not establish viewport or capture support. |
| OBS / Window Capture / Windows Graphics Capture | Chrome / Edge | 100%, 125%, 150%, 175%, 200% | Same scale / mixed scale | Not tested | Not tested | Separate from Display Capture; never infer from display-source results. |
| OBS / Window Capture / BitBlt | Chrome / Edge | 100%, 125%, 150%, 175%, 200% | Same scale / mixed scale | Not tested | Not tested | A window-only source may never include overlays; that is not an exclusion pass. |
| Other recorder or Automatic method | Any app | Any | Any | Not tested | Not tested | Record the effective method, recorder version and clip. |
| Any | Any browser | 100%, 125%, 200% | Any | Not tested | Not tested | These scaling values are unavailable on this desktop without changing Windows settings. |
| OBS Display Capture | Native pattern / browsers | 150% ↔ 175% | During recording | API rectangles only | Not tested | Static clips on one monitor do not prove recorded transitions. |

No claim is made for the edge magnifier, HDR, protected video, minimized windows, ARM64, other Windows builds, GPU/driver combinations, recorder output rescaling, or browser versions outside a completed row.

The [sanitized evidence summary](evidence/2026-10-18-recording-checks.json) records all six clip hashes, decoding results, native rectangles, environment, source fingerprints and incomplete attempts. Raw files remain local under `artifacts/recording-checks/`. Visual automation could not activate the desktop and `LockApp` was running; a locked desktop is suspected, not established as the sole cause of the black output. The harness now checks the input desktop before creating windows or recording. No row has passed saved-recording compatibility yet.

## Check your setup in about a minute

1. Set the frame or resize your app. In the recorder, note the exact capture method, canvas, output size and any encoder rescale setting. Keep browser zoom at the intended value.
2. Open the [test pattern](../tests/ExactFrame.WindowsChecks/fixture/index.html) in your browser. Keep all four colored corners and the one-pixel white edge visible. Turn on the outline, guides, control bar and other overlays you plan to use. Keep **Hide from recorders** on.
3. Record 5–10 seconds. Stop and open the saved video. Check its file dimensions, four corners, alignment, and the absence of the outline, guides, dimming, magnifier and control bar. Preview alone is insufficient.
4. Open **Check my setup** in ExactFrame's title bar. Enter recorder/version, effective capture method, browser/app version and zoom, and a clip reference. Mark only what you inspected, then **Copy setup report**. This is a user report, not an automatic pass.
5. Repeat after changing method, version, zoom, scale or monitor. For mixed-DPI work, record while moving in both directions and inspect before, during and after the transition. If exclusion fails, hide the outline/controls before recording.

The main ExactFrame window is not excluded. Move it outside the recording area or minimize it. Setup reports are copied locally and never uploaded by the app; entries are kept only while the dialog is open.

## Repeatable Windows checks

See [the Windows harness instructions](../tests/ExactFrame.WindowsChecks/README.md). Keep every failed/incomplete attempt. Promote a matrix row only after checking the named evidence. Public summaries belong alongside this matrix; raw clips may remain local with their SHA-256 hashes in the summary.

Microsoft documents the Boolean return value and failure reporting for [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity). API success covers supported Windows capture paths, not every recorder implementation. OBS documents [Display Capture](https://obsproject.com/kb/display-capture-sources), [Window Capture](https://obsproject.com/kb/window-capture-sources) and [launch parameters](https://obsproject.com/kb/launch-parameters).
