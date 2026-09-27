using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Outline color, line, dimming, guides and the on-screen controls.
public sealed partial class MainViewModel
{
    private static readonly string[] StyleProperties =
    [
        nameof(Style), nameof(ShowSizeLabel), nameof(DimOutside), nameof(DimOpacity), nameof(DimOpacityText),
        nameof(ShowThirds), nameof(ShowCenterMark), nameof(ShowSafeArea), nameof(ShowHud)
    ];

    private OutlineStyle _style;

    public OutlineStyle Style => _style;

    public IReadOnlyList<SwatchItem> Swatches { get; }

    public IReadOnlyList<ChoiceItem> Thicknesses { get; }

    public IReadOnlyList<ChoiceItem> Lines { get; }

    public bool ShowSizeLabel
    {
        get => _style.ShowSizeLabel;
        set => UpdateStyle(_style with { ShowSizeLabel = value });
    }

    public bool DimOutside
    {
        get => _style.DimOutside;
        set => UpdateStyle(_style with { DimOutside = value });
    }

    public double DimOpacity
    {
        get => _style.DimOpacityPercent;
        set
        {
            if (double.IsNaN(value)) return;
            UpdateStyle(_style with { DimOpacityPercent = (int)Math.Clamp(Math.Round(value), 10, 80) });
        }
    }

    public string DimOpacityText => $"{_style.DimOpacityPercent}%";

    public bool ShowThirds
    {
        get => _style.ShowThirds;
        set => UpdateStyle(_style with { ShowThirds = value });
    }

    public bool ShowCenterMark
    {
        get => _style.ShowCenterMark;
        set => UpdateStyle(_style with { ShowCenterMark = value });
    }

    public bool ShowSafeArea
    {
        get => _style.ShowSafeArea;
        set => UpdateStyle(_style with { ShowSafeArea = value });
    }

    public bool ShowHud
    {
        get => _style.ShowHud;
        set => UpdateStyle(_style with { ShowHud = value });
    }

    private void UpdateStyle(OutlineStyle style)
    {
        if (style == _style) return;
        _style = style;
        _settings.Style = style;
        _overlay.ApplyStyle(style);
        SaveSettings();

        SelectOnly(Swatches, s => s.Color == _style.Color);
        SelectOnly(Thicknesses, t => t.Key == _style.Thickness.ToString());
        SelectOnly(Lines, l => l.Key == _style.Line.ToString());
        foreach (string name in StyleProperties) OnPropertyChanged(name);
        NotifyStage();
    }
}
