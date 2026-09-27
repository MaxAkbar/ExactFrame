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

    public static SolidColorBrush ChipBrush(bool live) => BrushFromHex(live ? "#2434D5B5" : "#0FFFFFFF");

    public static SolidColorBrush ChipTextBrush(bool live) => BrushFromHex(live ? "#BFF3E6" : "#B8CBC9");

    public static SolidColorBrush ChipDotBrush(bool live) => BrushFromHex(live ? "#34D5B5" : "#6F8A88");

    public static SolidColorBrush SelectedBrush(bool selected) => BrushFromHex(selected ? "#0E7A6D" : "#5E706E");

    public static SolidColorBrush SelectedTileBrush(bool selected) => BrushFromHex(selected ? "#E2F2EE" : "#EEF2F1");

    public static SolidColorBrush NavBrush(bool selected) => BrushFromHex(selected ? "#0A5F55" : "#3C4F4D");

    public static FontFamily NavFont(bool selected) =>
        (FontFamily)Application.Current.Resources[selected ? "SansSemiBoldFont" : "SansMediumFont"];

    public static Windows.UI.Text.FontWeight NavWeight(bool selected) =>
        selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Medium;

    public static SolidColorBrush FitBrush(bool fits) => BrushFromHex(fits ? "#7FE0C9" : "#F5B84A");

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
