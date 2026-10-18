using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

public sealed partial class MainViewModel
{
    public AppThemePreference ThemePreference
    {
        // Older settings omit this value; unknown values from a newer version follow Windows.
        get => Enum.IsDefined(_settings.ThemePreference) ? _settings.ThemePreference : AppThemePreference.System;
        set
        {
            if (!Enum.IsDefined(value) || ThemePreference == value) return;
            _settings.ThemePreference = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeLabel));
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    public string ThemeLabel => $"Theme: {ThemePreference}";
    public bool IsSystemTheme => ThemePreference == AppThemePreference.System;
    public bool IsLightTheme => ThemePreference == AppThemePreference.Light;
    public bool IsDarkTheme => ThemePreference == AppThemePreference.Dark;
}
