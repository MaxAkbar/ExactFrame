using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ExactFrame.Core.Models;
using ExactFrame.Core.ViewModels;
using ExactFrame.Helpers;
using DRect = System.Drawing.Rectangle;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace ExactFrame.Controls;

/// <summary>
/// The to-scale preview of the selected display: wallpaper, taskbar, the frame with its style (dimming,
/// guides, label, handles) or, in Resize window mode, the window's current and target rectangles.
/// Redrawn from a <see cref="PreviewState"/> whenever it or the control's size changes.
/// </summary>
public sealed class DisplayMap : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(object), typeof(DisplayMap), new PropertyMetadata(null, (d, _) => ((DisplayMap)d).Redraw()));

    private readonly Canvas _canvas = new();
    private double _scale;
    private double _originX;
    private double _originY;
    private DRect _display;

    public DisplayMap()
    {
        Content = _canvas;
        IsTabStop = false;
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>A <see cref="PreviewState"/>.</summary>
    public object? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        if (State is not PreviewState state || ActualWidth <= 1 || ActualHeight <= 1) return;

        _display = state.DisplayBounds;
        _scale = Math.Min(ActualWidth / _display.Width, ActualHeight / _display.Height);
        double width = _display.Width * _scale, height = _display.Height * _scale;
        _originX = Math.Round((ActualWidth - width) / 2);
        _originY = 0;
        _canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(_originX, _originY, width, height) };

        AddBox(_originX, _originY, width, height, "#14272C", "#2A4147", 1, 8);
        if (!state.IsResizeMode) DrawDesktop();
        DrawTaskbar(state);

        if (state.IsResizeMode) DrawResize(state);
        else DrawOutline(state);
    }

    // ---- Scene --------------------------------------------------------------------------------

    private void DrawDesktop()
    {
        double w = _display.Width * _scale, h = _display.Height * _scale;
        var editor = AddBox(_originX + w * 0.05, _originY + h * 0.08, w * 0.56, h * 0.62, "#1A3137", "#24414A", 1, 5);
        AddBox(_originX + w * 0.05, _originY + h * 0.08, w * 0.56, 12, "#203C45", null, 0, 5);
        double[] lines = [0.38, 0.62, 0.54, 0.7, 0.3, 0.58, 0.44];
        for (int i = 0; i < lines.Length; i++)
        {
            AddBox(_originX + w * 0.05 + 18, _originY + h * 0.08 + 28 + i * 13, w * 0.56 * lines[i] * 0.85, 5,
                i % 4 == 0 ? "#2F5963" : "#29494F", null, 0, 3);
        }
        _ = editor;
        AddBox(_originX + w * 0.64, _originY + h * 0.2, w * 0.31, h * 0.48, "#182D33", "#223D45", 1, 5);
        AddBox(_originX + w * 0.64, _originY + h * 0.2, w * 0.31, 12, "#1E3840", null, 0, 5);
    }

    private void DrawTaskbar(PreviewState state)
    {
        var bounds = state.DisplayBounds;
        var work = state.WorkArea;
        DRect bar;
        if (work.Bottom < bounds.Bottom) bar = DRect.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
        else if (work.Top > bounds.Top) bar = DRect.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
        else if (work.Left > bounds.Left) bar = DRect.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
        else if (work.Right < bounds.Right) bar = DRect.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
        else return; // auto-hidden taskbar

        var (x, y, w, h) = Map(bar);
        AddBox(x, y, Math.Max(w, 2), Math.Max(h, 2), "#0A1619", null, 0, 0);

        bool horizontal = bar.Width >= bar.Height;
        double icon = Math.Clamp((horizontal ? h : w) * 0.5, 3, 8);
        for (int i = -2; i <= 2; i++)
        {
            double cx = horizontal ? x + w / 2 + i * (icon + 4) : x + w / 2;
            double cy = horizontal ? y + h / 2 : y + h / 2 + i * (icon + 4);
            AddBox(cx - icon / 2, cy - icon / 2, icon, icon, i == 0 ? "#1E7A6B" : "#2C4A51", null, 0, 2);
        }
    }

    private void DrawOutline(PreviewState state)
    {
        var style = state.Style;
        uint argb = state.IsFit ? OutlinePalette.Argb(style.Color) : 0xFFF5B84A;
        var color = Format.Opaque(argb);
        var (fx, fy, fw, fh) = Map(state.Frame);
        double mapW = _display.Width * _scale, mapH = _display.Height * _scale;

        if (style.DimOutside)
        {
            var dim = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)Math.Round(style.DimOpacityPercent * 2.55), 4, 10, 12));
            AddFill(_originX, _originY, mapW, Math.Max(0, fy - _originY), dim);
            AddFill(_originX, fy + fh, mapW, Math.Max(0, _originY + mapH - fy - fh), dim);
            AddFill(_originX, fy, Math.Max(0, fx - _originX), fh, dim);
            AddFill(fx + fw, fy, Math.Max(0, _originX + mapW - fx - fw), fh, dim);
        }

        double stroke = style.Thickness >= 6 ? 3 : style.Thickness >= 4 ? 2 : 1.5;
        var brush = new SolidColorBrush(color);
        var soft = new SolidColorBrush(Format.WithAlpha(argb, 110));

        if (style.ShowThirds)
        {
            for (int i = 1; i <= 2; i++)
            {
                AddFill(fx + fw * i / 3, fy, 1, fh, soft);
                AddFill(fx, fy + fh * i / 3, fw, 1, soft);
            }
        }
        if (style.ShowCenterMark)
        {
            AddFill(fx + fw / 2 - 9, fy + fh / 2, 18, 1, brush);
            AddFill(fx + fw / 2, fy + fh / 2 - 9, 1, 18, brush);
        }
        if (style.ShowSafeArea)
        {
            AddOutline(fx + fw * 0.05, fy + fh * 0.05, fw * 0.9, fh * 0.9, soft, 1, dashed: true);
        }

        if (style.Line == OutlineLine.Corners)
        {
            DrawCorners(fx, fy, fw, fh, brush, stroke);
        }
        else
        {
            AddOutline(fx, fy, fw, fh, brush, stroke, dashed: style.Line == OutlineLine.Dashed);
        }

        foreach (var nested in state.NestedFrames)
        {
            var (nx, ny, nw, nh) = Map(nested.Bounds);
            var nestedColor = Format.Opaque(OutlinePalette.Argb(nested.Color));
            var nestedBrush = new SolidColorBrush(nestedColor);
            if (style.Line == OutlineLine.Corners) DrawCorners(nx, ny, nw, nh, nestedBrush, stroke);
            else AddOutline(nx, ny, nw, nh, nestedBrush, stroke, dashed: style.Line == OutlineLine.Dashed);
            if (style.ShowSizeLabel && nw >= 68 && nh >= 26)
                AddText($"{nested.Bounds.Width} × {nested.Bounds.Height}", nx + 4, ny + 4,
                    OutlinePalette.Hex(nested.Color), 10);
        }

        if (state.ShowHandles)
        {
            foreach (var (hx, hy) in new[] { (fx, fy), (fx + fw, fy), (fx, fy + fh), (fx + fw, fy + fh) })
                AddBox(hx - 3.5, hy - 3.5, 7, 7, "#0F1E22", null, 0, 0, brush, 1.5);
        }

        if (style.ShowSizeLabel)
        {
            bool above = fy - _originY >= 20;
            AddLabel(state.SizeText, above ? fx : fx + 4, above ? fy - 19 : fy + 4, color);
        }

        if (style.ShowHud)
        {
            double hudY = fy + fh + 16 <= _originY + mapH - 16 ? fy + fh + 6 : fy + fh - 18;
            var hud = AddBox(fx + fw / 2 - 32, hudY, 64, 10, "#0B1A1E", "#24FFFFFF", 1, 4);
            _ = hud;
            AddBox(fx + fw / 2 - 26, hudY + 3.5, 20, 3, "#93AEAB", null, 0, 1.5);
            for (int i = 0; i < 3; i++) AddBox(fx + fw / 2 + i * 8, hudY + 3.5, 5, 3, "#5D7775", null, 0, 1.5);
        }
    }

    private void DrawResize(PreviewState state)
    {
        var mint = Format.BrushFromHex("#34D5B5");
        if (state.Ghost is { } ghost)
        {
            var (gx, gy, gw, gh) = Map(ghost);
            AddOutline(gx, gy, gw, gh, Format.BrushFromHex("#73B8CBC9"), 1, dashed: true);
            AddText($"Now {ghost.Width} × {ghost.Height}", gx + 6, gy + gh - 18, "#93AEAB", 10.5);
        }

        var (fx, fy, fw, fh) = Map(state.Frame);
        double titleBar = state.ShowWindowChrome ? Math.Max(4, state.TitleBarHeight * _scale) : Math.Max(4, 32 * _scale);
        double wy = state.ShowWindowChrome ? fy - titleBar : fy;
        double wh = state.ShowWindowChrome ? fh + titleBar : fh;

        AddBox(fx, wy, fw, wh, "#1C3238", "#3A565D", 1, 4);
        AddBox(fx, wy, fw, titleBar, "#27434B", null, 0, 4);
        AddBox(fx + 1, wy + titleBar, fw * 0.16, wh - titleBar - 1, "#182B30", null, 0, 0);
        double[] lines = [0.4, 0.66, 0.52, 0.74, 0.34, 0.58];
        for (int i = 0; i < lines.Length && titleBar + 14 + i * 11 < wh - 6; i++)
            AddBox(fx + fw * 0.16 + 12, wy + titleBar + 12 + i * 11, (fw * 0.84 - 24) * lines[i], 4, i % 4 == 0 ? "#3A6570" : "#2E5059", null, 0, 2);

        AddOutline(fx, fy, fw, fh, mint, 2, dashed: true);
        string label = (state.ShowWindowChrome ? "Client " : "Window ") + state.SizeText;
        AddLabel(label, fx + fw - 8 - MeasureLabel(label), fy + fh - 24, Format.ColorFromHex("#34D5B5"));
    }

    // ---- Drawing helpers --------------------------------------------------------------------

    private (double X, double Y, double W, double H) Map(DRect r) => (
        _originX + (r.X - _display.X) * _scale,
        _originY + (r.Y - _display.Y) * _scale,
        r.Width * _scale,
        r.Height * _scale);

    private Border AddBox(double x, double y, double w, double h, string fill, string? line, double thickness, double radius,
        Brush? lineBrush = null, double lineThickness = 0)
    {
        var box = new Border
        {
            Width = Math.Max(0, w),
            Height = Math.Max(0, h),
            Background = Format.BrushFromHex(fill),
            CornerRadius = new CornerRadius(radius),
            BorderBrush = lineBrush ?? (line is null ? null : Format.BrushFromHex(line)),
            BorderThickness = new Thickness(lineBrush is null ? thickness : lineThickness)
        };
        Place(box, x, y);
        return box;
    }

    private void AddFill(double x, double y, double w, double h, Brush fill)
    {
        if (w <= 0 || h <= 0) return;
        var rect = new Shapes.Rectangle { Width = w, Height = h, Fill = fill };
        Place(rect, x, y);
    }

    private void DrawCorners(double x, double y, double w, double h, Brush brush, double stroke)
    {
        double arm = Math.Clamp(Math.Min(w, h) / 5, 6, 18);
        double thickness = stroke + 0.5;
        AddFill(x, y, arm, thickness, brush); AddFill(x, y, thickness, arm, brush);
        AddFill(x + w - arm, y, arm, thickness, brush); AddFill(x + w - thickness, y, thickness, arm, brush);
        AddFill(x, y + h - thickness, arm, thickness, brush); AddFill(x, y + h - arm, thickness, arm, brush);
        AddFill(x + w - arm, y + h - thickness, arm, thickness, brush);
        AddFill(x + w - thickness, y + h - arm, thickness, arm, brush);
    }

    private void AddOutline(double x, double y, double w, double h, Brush stroke, double thickness, bool dashed)
    {
        var rect = new Shapes.Rectangle
        {
            Width = Math.Max(1, w),
            Height = Math.Max(1, h),
            Stroke = stroke,
            StrokeThickness = thickness
        };
        if (dashed) rect.StrokeDashArray = new DoubleCollection { 3, 2 };
        Place(rect, x, y);
    }

    private void AddLabel(string text, double x, double y, Windows.UI.Color fill)
    {
        var label = new Border
        {
            Height = 18,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(fill),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontFamily = (FontFamily)Application.Current.Resources["MonoSemiBoldFont"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Format.BrushFromHex("#06201C"),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Place(label, x, y);
    }

    private void AddText(string text, double x, double y, string color, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = Format.BrushFromHex(color) };
        Place(block, x, y);
    }

    private static double MeasureLabel(string text) => text.Length * 6.6 + 12;

    private void Place(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _canvas.Children.Add(element);
    }
}
