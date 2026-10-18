using ExactFrame.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExactFrame.Helpers;

internal static class AppThemeHelper
{
    public static ElementTheme ForPreference(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Light => ElementTheme.Light,
        AppThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    // Popups are outside the main visual tree. Give dialogs the owner's resolved theme explicitly.
    public static void MatchOwner(ContentDialog dialog)
    {
        if (dialog.XamlRoot?.Content is FrameworkElement owner) dialog.RequestedTheme = owner.ActualTheme;
    }
}
