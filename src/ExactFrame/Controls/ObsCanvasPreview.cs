using ExactFrame.Core.Obs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace ExactFrame.Controls;

/// <summary>A geometry preview of the proposed OBS canvas placement, not a screenshot of the captured display.</summary>
public sealed class ObsCanvasPreview : UserControl
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(nameof(Plan), typeof(object),
        typeof(ObsCanvasPreview), new PropertyMetadata(null, (d, _) => ((ObsCanvasPreview)d).Draw()));
    private readonly Canvas _canvas = new();
    public ObsCanvasPreview() { Content = _canvas; IsTabStop = false; SizeChanged += (_, _) => Draw(); }
    public object? Plan { get => GetValue(PlanProperty); set => SetValue(PlanProperty, value); }
    private void Draw()
    {
        _canvas.Children.Clear();
        if (Plan is not ObsPlan plan || ActualWidth < 1 || ActualHeight < 1) return;
        var video = plan.Before.Video;
        double scale = Math.Min(ActualWidth / video.CanvasWidth, ActualHeight / video.CanvasHeight);
        double x = (ActualWidth - video.CanvasWidth * scale) / 2, y = (ActualHeight - video.CanvasHeight * scale) / 2;
        Box(x, y, video.CanvasWidth * scale, video.CanvasHeight * scale, "#0F1E22", "#2A4147");
        Box(x + plan.X * scale, y + plan.Y * scale, plan.Width * scale, plan.Height * scale, "#164B48", "#34D5B5");
        var label = new TextBlock { Text = $"{plan.Region.Bounds.Width} × {plan.Region.Bounds.Height}",
            Foreground = Brush("#F2F8F7"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(1, plan.Width * scale - 12) };
        Canvas.SetLeft(label, x + plan.X * scale + 6); Canvas.SetTop(label, y + plan.Y * scale + 6);
        _canvas.Children.Add(label);
    }
    private void Box(double x, double y, double width, double height, string fill, string stroke)
    {
        var box = new Shapes.Rectangle { Width = width, Height = height, Fill = Brush(fill), Stroke = Brush(stroke), StrokeThickness = 1 };
        Canvas.SetLeft(box, x); Canvas.SetTop(box, y); _canvas.Children.Add(box);
    }
    private static SolidColorBrush Brush(string color) => new(Helpers.Format.ColorFromHex(color));
}
