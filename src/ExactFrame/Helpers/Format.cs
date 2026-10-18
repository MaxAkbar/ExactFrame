using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ExactFrame.Core.ViewModels;
using Windows.UI;

namespace ExactFrame.Helpers;

/// <summary>Small pure functions used from x:Bind in XAML.</summary>
public static class Format
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = [];
    private static readonly Dictionary<string, SolidColorBrush> ThemeBrushCache = [];
    private static ElementTheme _theme = ElementTheme.Default;
    private static bool _highContrast;

    // Programmatically built content shares mutable semantic brushes. Updating their colors also
    // updates existing help paragraphs, profile icons and keycaps on a live Windows theme change.
    public static void UpdateTheme(ElementTheme theme, bool highContrast = false)
    {
        _theme = theme;
        _highContrast = highContrast;
        foreach (var (key, brush) in ThemeBrushCache) brush.Color = ResolveThemeBrush(key).Color;
    }

    public static SolidColorBrush ThemeBrush(string key)
    {
        if (!ThemeBrushCache.TryGetValue(key, out var brush))
            ThemeBrushCache[key] = brush = new SolidColorBrush(ResolveThemeBrush(key).Color);
        return brush;
    }

    private static SolidColorBrush ResolveThemeBrush(string key)
    {
        string theme = _highContrast ? "HighContrast" : _theme == ElementTheme.Light ? "Light" : "Default";
        SolidColorBrush? Find(ResourceDictionary dictionary)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed) && themed is ResourceDictionary palette &&
                palette.TryGetValue(key, out var value) && value is SolidColorBrush brush) return brush;
            foreach (var merged in dictionary.MergedDictionaries)
                if (Find(merged) is { } found) return found;
            return null;
        }
        return Find(Application.Current.Resources) ?? (SolidColorBrush)Application.Current.Resources[key];
    }

    public static string Upper(string? value) => (value ?? string.Empty).ToUpperInvariant();

    public static bool Not(bool value) => !value;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static SolidColorBrush Brush(string? hex) => BrushFromHex(hex ?? "#000000");

    public static SolidColorBrush StatusBrush(StatusKind kind) => kind switch
    {
        StatusKind.Warning => BrushFromHex("#C77A0A"),
        StatusKind.Live => BrushFromHex("#0E7A6D"),
        StatusKind.Busy => BrushFromHex("#2F6FB0"),
        _ => BrushFromHex("#8FA19F")
    };

    public static SolidColorBrush StatusHalo(StatusKind kind) => kind switch
    {
        StatusKind.Warning => BrushFromHex("#2EC77A0A"),
        StatusKind.Live => BrushFromHex("#2E0E7A6D"),
        StatusKind.Busy => BrushFromHex("#2E2F6FB0"),
        _ => BrushFromHex("#338FA19F")
    };

    public static SolidColorBrush PrimaryBrush(bool isHide) => BrushFromHex(isHide ? "#122422" : "#0E7A6D");

    public static SolidColorBrush ChipBrush(bool live) => ThemeBrush(live ? "MintTintBrush" : "StageChipBrush");

    public static SolidColorBrush ChipTextBrush(bool live) => ThemeBrush(live ? "LiveTextBrush" : "StageSoftBrush");

    public static SolidColorBrush ChipDotBrush(bool live) => ThemeBrush(live ? "MintBrush" : "IdleDotBrush");

    public static SolidColorBrush SelectedBrush(bool selected) => ThemeBrush(selected ? "AccentStrongBrush" : "MutedBrush");

    public static SolidColorBrush SelectedTileBrush(bool selected) => ThemeBrush(selected ? "AccentTintBrush" : "TrackBrush");

    public static SolidColorBrush NavBrush(bool selected) => ThemeBrush(selected ? "AccentStrongBrush" : "InkSoftBrush");

    public static FontFamily NavFont(bool selected) =>
        (FontFamily)Application.Current.Resources[selected ? "SansSemiBoldFont" : "SansMediumFont"];

    public static Windows.UI.Text.FontWeight NavWeight(bool selected) =>
        selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Medium;

    public static SolidColorBrush FitBrush(bool fits) => ThemeBrush(fits ? "MintTextBrush" : "StageWarnBrush");

    public static string FitGlyph(bool fits) => fits ? "\uE73E" : "\uE7BA";

    internal static Color ColorFromHex(string hex)
    {
        string value = hex.TrimStart('#');
        if (value.Length == 6) value = "FF" + value;
        uint argb = Convert.ToUInt32(value, 16);
        return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }

    internal static SolidColorBrush BrushFromHex(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush(ColorFromHex(hex));
            BrushCache[hex] = brush;
        }
        return brush;
    }

    internal static Color WithAlpha(uint argb, byte alpha) =>
        Color.FromArgb(alpha, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    internal static Color Opaque(uint argb) => WithAlpha(argb, 255);

    internal static readonly Color Transparent = Colors.Transparent;
}
