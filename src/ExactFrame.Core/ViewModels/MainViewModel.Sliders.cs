using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Width and height sliders. The range covers sizes that fit the selected display, dragging snaps to
// even pixels (what video encoders expect) and pulls toward standard sizes so 1920 × 1080 is easy to hit.
public sealed partial class MainViewModel
{
    /// <summary>Keyboard step for the sliders. Moves this small never snap to a standard size.</summary>
    public const int SliderSmallChange = 2;

    /// <summary>How close to a standard size a drag must land to snap to it, as a share of the slider range.</summary>
    private const double SnapShare = 0.015;

    /// <summary>
    /// Largest width that fits the usable area (keeping the aspect ratio when it's locked), and never less than
    /// the current width, so a slider bound to it never clamps a typed value.
    /// </summary>
    public double WidthSliderMaximum => Math.Max(SliderFit().Width, _width);

    /// <summary>Largest height that fits, and never less than the current height.</summary>
    public double HeightSliderMaximum => Math.Max(SliderFit().Height, _height);

    /// <summary>Applies a width from the slider: snaps it, keeps the aspect ratio when locked and leaves typed values alone.</summary>
    public void SetWidthFromSlider(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        var fit = SliderFit();
        int width = FromSlider(value, _width, SnapTargets(p => p.Width, fit.Width), (int)WidthSliderMaximum);
        if (width == _width) return;

        int height = _aspectLocked ? ClampEven(width / _ratio) : _height;
        ApplySize(width, height, updateRatio: !_aspectLocked);
    }

    /// <summary>Applies a height from the slider. See <see cref="SetWidthFromSlider"/>.</summary>
    public void SetHeightFromSlider(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        var fit = SliderFit();
        int height = FromSlider(value, _height, SnapTargets(p => p.Height, fit.Height), (int)HeightSliderMaximum);
        if (height == _height) return;

        int width = _aspectLocked ? ClampEven(height * _ratio) : _width;
        ApplySize(width, height, updateRatio: !_aspectLocked);
    }

    /// <summary>The largest size that fits the usable area of the selected display.</summary>
    private Size SliderFit()
    {
        var area = _display?.UsableArea(_keepClear).Size ?? new Size(MaxSize, MaxSize);
        int width = area.Width;
        int height = area.Height;
        if (_aspectLocked)
        {
            width = Math.Min(width, (int)Math.Floor(area.Height * _ratio));
            height = Math.Min(height, (int)Math.Floor(area.Width / _ratio));
        }
        return new Size(Math.Clamp(width, MinSize, MaxSize), Math.Clamp(height, MinSize, MaxSize));
    }

    private static IEnumerable<int> SnapTargets(Func<ResolutionPreset, int> dimension, int fit) =>
        ResolutionPreset.All.Where(p => !p.IsCustom).Select(dimension).Append(fit).Distinct();

    private static int FromSlider(double value, int current, IEnumerable<int> targets, int maximum)
    {
        int pixels = (int)Math.Round(value);
        int delta = pixels - current;

        if (Math.Abs(delta) > SliderSmallChange * 2)
        {
            int tolerance = Math.Max(4, (int)Math.Round((maximum - MinSize) * SnapShare));
            int nearest = targets.Where(t => t >= MinSize && t <= maximum)
                .DefaultIfEmpty(int.MinValue)
                .MinBy(t => Math.Abs((long)t - pixels));
            if (nearest != int.MinValue && Math.Abs(nearest - pixels) <= tolerance) return nearest;
        }

        // Round odd values in the direction of travel so the arrow keys never get stuck.
        if (pixels % 2 != 0) pixels += delta >= 0 ? 1 : -1;
        return Math.Clamp(pixels, MinSize, Math.Max(MinSize, maximum));
    }

    private static int ClampEven(double value) =>
        (int)Math.Clamp(Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2, MinSize, MaxSize);

    private void NotifySliderRanges()
    {
        OnPropertyChanged(nameof(WidthSliderMaximum));
        OnPropertyChanged(nameof(HeightSliderMaximum));
    }
}
