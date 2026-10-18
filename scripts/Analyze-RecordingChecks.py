"""Inspect controlled pattern recordings, not arbitrary videos. Requires ffmpeg and ffprobe on PATH.

Decode at 2 fps throughout every clip. Check file dimensions/corner markers, then compare excluded
and visible overlays against the baseline from the same capture method. A positive visible control
is required so a source that never captured the overlays cannot produce a false exclusion pass.
"""
import argparse
import hashlib
import json
import pathlib
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("directory", type=pathlib.Path)
args = parser.parse_args()
directory = args.directory.resolve()
manifest = json.loads((directory / "windows-checks.json").read_text(encoding="utf-8-sig"))
if manifest["error"]:
    raise SystemExit("Native/recorder run is incomplete; preserve it and collect a new run.")

WIDTH, HEIGHT = 1280, 800
FRAME_BYTES = WIDTH * HEIGHT * 3


def decode(path):
    probe = json.loads(subprocess.check_output(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries",
        "stream=codec_name,width,height,avg_frame_rate:format=duration", "-of", "json", str(path)], text=True))
    stream = probe["streams"][0]
    if (stream["width"], stream["height"]) != (WIDTH, HEIGHT):
        raise ValueError(f"Wrong encoded dimensions: {path.name}: {stream}")
    raw = subprocess.check_output(["ffmpeg", "-v", "error", "-i", str(path), "-vf", "fps=2", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"])
    if len(raw) % FRAME_BYTES or len(raw) < 4 * FRAME_BYTES:
        raise ValueError(f"Too few complete decoded frames: {path.name}")
    frames = [raw[i:i + FRAME_BYTES] for i in range(0, len(raw), FRAME_BYTES)]
    return probe, frames


def corners_match(frame):
    # Markers are 32 DIPs; sample well inside them, away from codec edge ringing.
    for (x, y), expected in zip([(12, 12), (WIDTH-13, 12), (12, HEIGHT-13), (WIDTH-13, HEIGHT-13)],
                               [(255,0,0),(0,255,0),(255,255,0),(0,255,255)]):
        actual = frame[(y*WIDTH+x)*3:(y*WIDTH+x)*3+3]
        if max(abs(a-b) for a,b in zip(actual, expected)) > 24:
            return False
    return True


def difference(reference, frame, rect):
    left, top, right, bottom = rect
    left, top, right, bottom = max(0,left), max(0,top), min(WIDTH,right), min(HEIGHT,bottom)
    errors = []
    # Sample every second pixel. A pixel differs when any channel exceeds 18 levels.
    for y in range(top, bottom, 2):
        for x in range(left, right, 2):
            p = (y*WIDTH+x)*3
            errors.append(max(abs(reference[p+c]-frame[p+c]) for c in range(3)))
    if not errors:
        raise ValueError("Empty comparison region")
    return sum(e > 18 for e in errors) / len(errors)


results = []
for method in ["DXGI", "WGC"]:
    entries = {c["phase"]: c for c in manifest["clips"] if c["method"] == method}
    if set(entries) != {"baseline", "visible", "excluded"}:
        raise ValueError(f"Missing baseline/positive/excluded clips for {method}")
    decoded = {}
    files = []
    for phase, entry in entries.items():
        path = directory / entry["clip"]
        probe, frames = decode(path)
        decoded[phase] = frames
        files.append({"phase": phase, "file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                      "sampledFrames": len(frames), "probe": probe})
    baseline = decoded["baseline"][0]
    hb = entries["visible"]["hudBounds"]
    desktop = entries["visible"]["desktopBounds"]
    hud = (hb["X"]-desktop["X"], hb["Y"]-desktop["Y"], hb["X"]-desktop["X"]+hb["Width"], hb["Y"]-desktop["Y"]+hb["Height"])
    visible_overlay = max(difference(baseline, f, (140,100,1100,640)) for f in decoded["visible"])
    visible_hud = max(difference(baseline, f, hud) for f in decoded["visible"])
    excluded_difference = max(difference(baseline, f, (0,0,WIDTH,HEIGHT)) for f in decoded["excluded"])
    all_corners = all(corners_match(f) for phase in ("baseline", "excluded") for f in decoded[phase])
    positive_control = visible_overlay > .01 and visible_hud > .10
    absent = excluded_difference < .001
    accepted = entries["excluded"]["overlayAccepted"] and "Windows accepted" in entries["excluded"]["controlBarStatus"]
    results.append({"method": method, "files": files, "cornerMarkersPresent": all_corners,
                    "visibleOverlayDifferenceFraction": visible_overlay, "visibleHudDifferenceFraction": visible_hud,
                    "excludedDifferenceFraction": excluded_difference, "positiveControlPassed": positive_control,
                    "excludedMatchesBaseline": absent,
                    "excludedOverlaysAbsent": absent if positive_control and all_corners else None,
                    "windowsAcceptedExclusion": accepted,
                    "boundedPatternCheckPassed": all_corners and positive_control and absent and accepted})

report = {"scope": "1280x800 static native pattern, 2 decoded frames per second, listed monitor/method only; magnifier, browsers and recorded transitions excluded",
          "ffprobeVersion": subprocess.check_output(["ffprobe", "-version"], text=True).splitlines()[0],
          "results": results, "passed": all(r["boundedPatternCheckPassed"] for r in results)}
(directory / "recorded-output-analysis.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, indent=2))
raise SystemExit(0 if report["passed"] else 1)
