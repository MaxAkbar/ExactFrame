using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ExactFrame.Helpers;

namespace ExactFrame.Controls;

/// <summary>Renders a shortcut as keycaps, e.g. [Ctrl] [Alt] [F8]. Variants: light (default), dark and on-accent.</summary>
public sealed class KeyChips : UserControl
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(object), typeof(KeyChips), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(string), typeof(KeyChips), new PropertyMetadata("Light", OnChanged));

    public static readonly DependencyProperty IsWarningProperty = DependencyProperty.Register(
        nameof(IsWarning), typeof(bool), typeof(KeyChips), new PropertyMetadata(false, OnChanged));

    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 3 };

    public KeyChips()
    {
        Content = _panel;
        IsTabStop = false;
    }

    /// <summary>An <c>IEnumerable&lt;string&gt;</c> of key names.</summary>
    public object? Keys
    {
        get => GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    /// <summary><c>Light</c>, <c>Dark</c> (on the HUD) or <c>OnAccent</c> (inside the teal button).</summary>
    public string Variant
    {
        get => (string)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public bool IsWarning
    {
        get => (bool)GetValue(IsWarningProperty);
        set => SetValue(IsWarningProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((KeyChips)d).Rebuild();

    private void Rebuild()
    {
        _panel.Children.Clear();
        if (Keys is not IEnumerable<string> keys) return;

        var (fill, line, text) = (Variant, IsWarning) switch
        {
            (_, true) => ("#FDF5E6", "#E0B25C", "#8A5100"),
            ("Dark", _) => ("#12FFFFFF", "#29FFFFFF", "#D5E3E1"),
            ("OnAccent", _) => ("#2EFFFFFF", "#00FFFFFF", "#FFFFFF"),
            _ => ("#F6F8F7", "#CCD6D4", "#3C4F4D")
        };

        foreach (string key in keys)
        {
            _panel.Children.Add(new Border
            {
                MinHeight = 22,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Background = Format.BrushFromHex(fill),
                BorderBrush = Format.BrushFromHex(line),
                Child = new TextBlock
                {
                    Text = key,
                    FontSize = 11,
                    FontFamily = (FontFamily)Application.Current.Resources["MonoMediumFont"],
                    Foreground = Format.BrushFromHex(text),
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
    }
}
