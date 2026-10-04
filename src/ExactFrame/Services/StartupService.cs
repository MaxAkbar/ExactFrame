using Microsoft.Win32;
using ExactFrame.Core.Services;

namespace ExactFrame.Services;

/// <summary>
/// Starts ExactFrame at sign-in through the current user's Run key, with <see cref="BackgroundArgument"/> so it opens
/// in the notification area. Windows' Startup apps setting can turn the entry off without removing it; that is read
/// as off, and turning it on again clears it.
/// </summary>
internal sealed class StartupService : IStartupService
{
    public const string BackgroundArgument = "--background";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "ExactFrame";

    /// <summary>On only when the entry starts this exe, so a moved or second copy shows off until it's turned on.</summary>
    public bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string command || command != Command()) return false;

            // The first byte is odd when the entry is turned off in Settings or Task Manager.
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);

        using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) run.SetValue(ValueName, Command());
        else run.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string Command() =>
        $"\"{Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ExactFrame.exe")}\" {BackgroundArgument}";
}
