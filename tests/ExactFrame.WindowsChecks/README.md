# Opt-in Windows recording checks

Run on an unlocked Windows desktop. This separate test executable is not shipped inside ExactFrame or run by Core tests. It compiles the production `WindowService`, `TitleBarDetector`, `OutlineOverlay`, native overlay windows and floating control bar directly from the app sources. Persistence, hotkeys, prompts and startup services use test doubles to avoid personal settings. The window and overlay implementations under test are real.

The harness creates and moves its own 1280×720/800 pattern window through the connected monitors. Do not use this while other work is being recorded. It does not change Windows scaling or display arrangement. It needs a 1400×1000 work area for recorder checks.

From the repository root, with .NET SDK 10.0.402 and the Windows SDK available:

```powershell
pwsh -NoProfile -File scripts/Run-WindowsRecordingChecks.ps1
```

Every run uses a new directory under `artifacts/recording-checks/`. Read `windows-checks.json`: compare the request, production measurement, independent native rectangle and DPI. Whole-window checks use DWM extended frame bounds; client checks use `GetClientRect` plus `ClientToScreen`. A whole-window rectangle includes chrome absent from its client rectangle. Neither measurement proves the recorded result.

## Browser measurements

Start the loopback fixture server in a separate terminal:

```powershell
pwsh -NoProfile -File scripts/Serve-RecordingFixture.ps1
```

Open `http://127.0.0.1:4477/` in a disposable Chrome or Edge window/profile at 100% zoom. Identify that browser window's process ID. Exactly one window in that process must start with the title **ExactFrame recording test pattern**; the harness refuses ordinary browser windows. It resizes only that window and restores its original placement afterward.

```powershell
pwsh -NoProfile -File scripts/Run-WindowsRecordingChecks.ps1 `
  -BrowserProcessId 12345 -BrowserMetricsFile artifacts/recording-browser/metrics.json
```

The fixture posts its CSS viewport, device pixel ratio and rounded physical viewport dimensions to the local server. Web page checks compare the production rectangle with these independent DOM dimensions. Metrics must be less than three seconds old. Keep only one fixture page open while collecting metrics. The executable version and DOM user agent are recorded. These checks do not verify browser recording or output pixels.

## Actual recorder output

Use a separate **portable** OBS install/profile and scene collection under `artifacts/`. Name the profile **ExactFrame QA**. Never point the harness at a personal recorder: it rejects other profile names, then modifies the test profile's canvas/output, recording path, sources and scene selection. The profile name is a guard, not proof of isolation.

Use simple output mode, x264, High Quality recording and MKV. Enable authenticated WebSockets on a test port such as 4456. Start OBS from its `bin/64bit` working directory with `--portable --profile "ExactFrame QA" --collection "ExactFrame QA"`. Resolve ordinary startup dialogs yourself; the harness never bypasses permission or security screens. See [OBS launch parameters](https://obsproject.com/kb/launch-parameters). Stop all outputs first.

Use `ffmpeg`, `ffprobe` and Python 3 on PATH. Supply the test instance's local WebSocket config (contains its password; never commit or share it):

```powershell
pwsh -NoProfile -File scripts/Run-WindowsRecordingChecks.ps1 `
  -ObsConfigFile artifacts/obs-reference/obs-portable/config/obs-studio/plugin_config/obs-websocket/config.json `
  -ObsPort 4456
```

All WASAPI audio inputs in the isolated recorder are muted before recording. The harness selects the leftmost sufficiently large display and crops to its own 1280×800 pattern. Separate Display Capture sources use explicit DXGI and Windows Graphics Capture methods. Each method gets three short clips, in order:

1. Baseline: no overlays.
2. Positive control: outline, nested guide, thirds, center/safe-area guides, dimming and real floating control bar visible; exclusion off.
3. Exclusion: the same overlays remain visible on the desktop; exclusion on.

`Analyze-RecordingChecks.py` decodes at two frames per second throughout all clips, checks 1280×800 file dimensions and corner markers, requires visible outline/control bar differences in the positive control, then compares excluded frames with baseline. Pixels differing by more than 18 RGB levels must cover less than 0.1% of sampled pixels. The threshold is for these static lossy clips only; it does not prove every frame or content type. Full clips and SHA-256 hashes are retained. Inspect at least one decoded frame per phase manually too.

The magnifier is excluded from this automated run because its visibility depends on the physical pointer. Test it separately with the pointer near an edge. Window Capture, browser clips and monitor transitions during recording are separate matrix rows. Stop the fixture server and close the owned OBS instance afterward.

The runner requests cleanup if its own recording might remain active, including on timeout. Cleanup requires the same isolated profile and an ownership marker in this run's directory. It does not stop an already-active output rejected by the initial checks. If cleanup cannot reach OBS, stop the isolated recorder manually. An inaccessible or non-default input desktop aborts the harness before any fixture or recording is created.

## Evidence policy

New runs preserve a copy of the source files and selected compiled binaries with hashes, so a later code change cannot silently replace the tested inputs. The earlier diagnostic attempts linked by the matrix predate this snapshot addition; they retain source fingerprints and clips but are not compatibility passes.

Preserve the source manifest, build output, Windows results, clips, analysis, logs and failures. Never overwrite an earlier attempt. Correct a harness/setup fault, retain its explanation, then create a fresh run. Public summaries must exclude passwords, personal window titles, profile paths and audio; include source fingerprints, environment, clip hashes, scope and limitations. Update [the matrix](../../docs/recording-compatibility.md) only from the evidence collected.
