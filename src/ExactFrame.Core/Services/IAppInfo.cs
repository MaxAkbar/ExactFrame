namespace ExactFrame.Core.Services;

/// <summary>Facts about this copy of ExactFrame and the machine it runs on, for the About page.</summary>
public interface IAppInfo
{
    string Version { get; }

    string RuntimeVersion { get; }

    string WindowsAppSdkVersion { get; }

    string OperatingSystem { get; }

    string SettingsPath { get; }
}
