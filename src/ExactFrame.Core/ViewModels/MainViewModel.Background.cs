using System.Security;

namespace ExactFrame.Core.ViewModels;

// Running in the background: closing to the notification area, and starting with Windows.
public sealed partial class MainViewModel
{
    /// <summary>Closing the main window leaves ExactFrame in the notification area, so shortcuts and the outline keep working.</summary>
    public bool KeepRunningInBackground
    {
        get => _settings.KeepRunningInBackground;
        set
        {
            if (_settings.KeepRunningInBackground == value) return;
            _settings.KeepRunningInBackground = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            if (_startup.IsEnabled == value) return;
            try
            {
                _startup.SetEnabled(value);
                SetStatus(value
                    ? "ExactFrame will start in the notification area when you sign in."
                    : "ExactFrame won’t start when you sign in.");
            }
            catch (Exception ex) when (IsExpected(ex) || ex is SecurityException)
            {
                SetStatus($"Start with Windows couldn’t be changed: {ex.Message}", warning: true);
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Saves the session when the main window closes to the notification area, in case Windows ends the app
    /// before it quits. Returns true the first time, so the app can say where it went.
    /// </summary>
    public bool MoveToBackground()
    {
        _settings.LastFrame = Snapshot("last-session", "Last session");
        bool first = !_settings.BackgroundHintShown;
        _settings.BackgroundHintShown = true;
        SaveSettings();
        return first;
    }
}
