using System.Reflection;
using System.Runtime.InteropServices;
using ExactFrame.Core.Services;

namespace ExactFrame.Services;

internal sealed class AppInfo(string databasePath) : IAppInfo
{
    public string Version { get; } = ReadVersion();

    public string RuntimeVersion { get; } = Environment.Version.ToString();

    public string WindowsAppSdkVersion { get; } = ReadWindowsAppSdkVersion();

    public string OperatingSystem { get; } = DescribeWindows();

    public string DatabasePath { get; } = databasePath;

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational)) return informational.Split('+')[0]; // drop the commit hash
        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static string ReadWindowsAppSdkVersion()
    {
        try
        {
            return Microsoft.Windows.ApplicationModel.WindowsAppRuntime.RuntimeInfo.AsString;
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or DllNotFoundException or EntryPointNotFoundException)
        {
            return "unknown";
        }
    }

    private static string DescribeWindows()
    {
        var version = Environment.OSVersion.Version;
        string name = version.Major == 10 && version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} (build {version.Build}, {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})";
    }
}
