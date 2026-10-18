using System.Diagnostics;
using System.Text.Json;
using ExactFrame.Core.Models;
using ExactFrame.Services;

namespace ExactFrame.WindowsChecks;

internal static class BrowserChecks
{
    public static async Task<bool> Run(WindowService windows, IReadOnlyList<DisplayInfo> monitors, int pid, List<object> checks, string? metricsPath)
    {
        // Opt in to one disposable fixture window. Refuse ordinary browser sessions and personal tabs.
        var targets = windows.GetWindows().Where(w => w.ProcessId == pid && w.Title.StartsWith("ExactFrame recording test pattern", StringComparison.Ordinal)).ToArray();
        if (targets.Length != 1) throw new InvalidOperationException("Expected exactly one disposable browser window titled ExactFrame recording test pattern.");
        var target = targets[0];
        using var process = Process.GetProcessById(pid);
        string? version = process.MainModule?.FileVersionInfo.FileVersion;
        bool passed = true;
        try
        {
            foreach (var monitor in monitors.Where(m => m.WorkArea.Width >= 1500 && m.WorkArea.Height >= 1000))
            {
                var desired = new System.Drawing.Rectangle(monitor.WorkArea.X + 80, monitor.WorkArea.Y + 160, 1280, 720);
                foreach (var area in new[] { WindowArea.VisibleFrame, WindowArea.Client, WindowArea.PageContent })
                {
                    try
                    {
                        var result = await windows.ResizeAsync(target, desired, area, CancellationToken.None);
                        JsonElement? metrics = null;
                        bool? pageDimensionsMatch = null;
                        if (area == WindowArea.PageContent && metricsPath is not null)
                        {
                            await Task.Delay(800);
                            using var document = JsonDocument.Parse(File.ReadAllText(metricsPath));
                            metrics = document.RootElement.Clone();
                            var fresh = DateTimeOffset.UtcNow - metrics.Value.GetProperty("utc").GetDateTimeOffset() < TimeSpan.FromSeconds(3);
                            pageDimensionsMatch = fresh && metrics.Value.GetProperty("physicalWidth").GetInt32() == result.Actual.Width &&
                                metrics.Value.GetProperty("physicalHeight").GetInt32() == result.Actual.Height;
                        }
                        checks.Add(new { kind = "browserResize", target.ProcessName, version, monitor = monitor.Number, monitor.ScalePercent,
                            area = area.ToString(), desired, result.Actual, result.Exact, pageMetrics = metrics, pageDimensionsMatch,
                            evidence = "Native measurement only; compare Web page bounds to the fixture page report and inspect a recording separately." });
                        passed &= result.Exact && pageDimensionsMatch != false;
                    }
                    catch (InvalidOperationException ex)
                    { passed = false; checks.Add(new { kind = "browserResize", target.ProcessName, version, monitor = monitor.Number, area = area.ToString(), status = "unavailable", reason = ex.Message }); }
                }
            }
        }
        finally { if (windows.CanRestore(target)) await windows.RestoreAsync(target, CancellationToken.None); }
        return passed;
    }
}
