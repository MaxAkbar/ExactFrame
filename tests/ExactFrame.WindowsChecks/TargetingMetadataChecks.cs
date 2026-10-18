using System.Diagnostics;
using ExactFrame.Core.Models;
using ExactFrame.Services;

namespace ExactFrame.WindowsChecks;

/// <summary>Read-only native discovery check. Records counts and results, without window titles or paths.</summary>
internal static class TargetingMetadataChecks
{
    internal static bool Run(WindowService service, List<object> checks)
    {
        var windows = service.GetWindows();
        int withPath = windows.Count(window => window.ExecutablePath is not null);
        int withClass = windows.Count(window => window.ClassName is not null);
        int independentlyChecked = 0, unavailableForIndependentCheck = 0;
        bool passed = windows.Count > 0 && withPath > 0 && withClass > 0;
        foreach (var window in windows.Where(window => window.ExecutablePath is not null))
        {
            try
            {
                using var process = Process.GetProcessById((int)window.ProcessId);
                string? path = process.MainModule?.FileName;
                if (path is null) { unavailableForIndependentCheck++; continue; }
                independentlyChecked++;
                passed &= string.Equals(path, window.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { unavailableForIndependentCheck++; }
        }
        int matchChecks = 0;
        foreach (var window in windows.Where(window => window.ExecutablePath is not null && window.ClassName is not null && window.Title.Length <= 2048))
        {
            var rule = new WindowMatchCriteria
            { ExecutablePath = window.ExecutablePath, ClassName = window.ClassName, Title = window.Title, TitleMode = WindowTitleMatchMode.Exact };
            if (WindowMatcher.ValidationError(rule) is not null) continue;
            matchChecks++;
            passed &= WindowMatcher.Matches(window, window.ProcessName, rule);
            passed &= !WindowMatcher.Matches(window with { ExecutablePath = null }, window.ProcessName, rule);
            passed &= !WindowMatcher.Matches(window with { ClassName = null }, window.ProcessName, rule);
            passed &= !WindowMatcher.Matches(window with { Title = window.Title + " changed" }, window.ProcessName, rule);
        }
        passed &= independentlyChecked > 0 && matchChecks > 0;
        checks.Add(new { kind = "targetingMetadata", windowCount = windows.Count, withPath, withClass,
            independentlyCheckedPaths = independentlyChecked, unavailableForIndependentCheck, matchChecks,
            pass = passed, resizedWindows = 0, note = "Read-only discovery; no titles, paths, screenshots or settings retained." });
        return passed;
    }
}
