using System.Diagnostics;
using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Settings;
using ExactFrame.Native;
using ExactFrame.Services;
using Microsoft.UI.Dispatching;

namespace ExactFrame.WindowsChecks;

/// <summary>Bounded desktop measurements, using production implementations and a separate database.</summary>
internal static class ResponsivenessChecks
{
    public static async Task Run(WindowService windows, WindowInfo fixture, DisplayInfo display,
        string output, List<object> checks, string? browserTitle)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var heartbeat = dispatcher.CreateTimer();
        heartbeat.Interval = TimeSpan.FromMilliseconds(16);
        var gaps = new List<double>();
        var observations = new List<string>();
        void Observed(object? sender, WindowObservation observation) => observations.Add(observation.Kind.ToString());
        windows.TrackedWindowChanged += Observed;
        long last = 0;
        void Tick(DispatcherQueueTimer _, object args)
        {
            long now = Stopwatch.GetTimestamp();
            if (last != 0) gaps.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
        }
        heartbeat.Tick += Tick;

        async Task Measure(string name, Func<List<double>, Task> action, double budget)
        {
            var samples = new List<double>();
            gaps.Clear(); observations.Clear();
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes();
            var started = Stopwatch.StartNew();
            last = Stopwatch.GetTimestamp();
            heartbeat.Start();
            try { await action(samples); await Task.Delay(32); }
            finally { heartbeat.Stop(); }
            checks.Add(new { kind = "responsiveness", scenario = name, budgetMs = budget,
                elapsedMs = started.Elapsed.TotalMilliseconds,
                cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
                operationMs = samples.ToArray(), dispatcherGapMs = gaps.ToArray(),
                overBudget = samples.Count(t => t > budget), observationKinds = observations.ToArray(),
                note = "Operation duration and 16 ms dispatcher heartbeat gaps; diagnostic evidence, not a performance guarantee." });
        }

        try
        {
            await Measure("idle", _ => Task.Delay(3000), 50);
            await Measure("native-client-tracking", async samples =>
            {
                windows.TrackingMeasured = samples.Add;
                try { windows.StartTracking(fixture, WindowArea.Client); await Task.Delay(5000); }
                finally { windows.StopTracking(); windows.TrackingMeasured = null; }
            }, 50);

            if (browserTitle is not null)
            {
                var targets = windows.GetWindows().Where(w => w.Title == browserTitle &&
                    (w.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) || w.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase))).ToArray();
                if (targets.Length != 1) throw new InvalidOperationException("Expected exactly one Chrome or Edge window with the supplied exact title. No window was resized.");
                await Measure("browser-page-tracking", async samples =>
                {
                    windows.TrackingMeasured = samples.Add;
                    try { windows.StartTracking(targets[0], WindowArea.PageContent); await Task.Delay(5000); }
                    finally { windows.StopTracking(); windows.TrackingMeasured = null; }
                }, 50);
            }
            else checks.Add(new { kind = "responsiveness", scenario = "browser-page-tracking", status = "not-tested", reason = "Supply --performance-browser-title for a visible disposable public browser page." });

            await Measure("magnifier-100ms", async samples =>
            {
                using var magnifier = new EdgeMagnifierWindow();
                var pointer = new Point(display.WorkArea.Left + 160, display.WorkArea.Top + 160);
                var frame = new Rectangle(pointer.X, pointer.Y, 640, 360);
                for (int i = 0; i < 50; i++)
                {
                    long begin = Stopwatch.GetTimestamp();
                    // Production capture/render path. Screen pixels remain in memory and are never saved.
                    magnifier.Update(frame, new Point(pointer.X, pointer.Y + i % 20), FrameEdge.Left, display.Bounds);
                    samples.Add(Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
                    await Task.Delay(100);
                }
            }, 100);

            using var store = new CSharpDbAppStore(Path.Combine(output, "performance-settings.db"));
            var settings = new AppSettings();
            await Measure("settings-cold-load", async samples =>
            {
                long begin = Stopwatch.GetTimestamp();
                settings = store.Load();
                samples.Add(Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
                await Task.Delay(100);
            }, 16);
            await Measure("settings-first-save-after-load", async samples =>
            {
                long begin = Stopwatch.GetTimestamp();
                store.Save(settings);
                samples.Add(Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
                await Task.Delay(100);
            }, 16);
            await Measure("settings-repeated-save", async samples =>
            {
                for (int i = 0; i < 40; i++)
                {
                    settings.SnapToEdges = i % 2 == 0;
                    long begin = Stopwatch.GetTimestamp();
                    store.Save(settings);
                    samples.Add(Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
                    await Task.Delay(50);
                }
            }, 16);
        }
        finally { windows.TrackedWindowChanged -= Observed; heartbeat.Stop(); heartbeat.Tick -= Tick; windows.TrackingMeasured = null; windows.StopTracking(); }
    }
}
