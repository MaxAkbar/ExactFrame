using ExactFrame.Core.Geometry;
using ExactFrame.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace ExactFrame.Controls;

/// <summary>Fits the source crop uniformly into the requested output, including any empty bars.</summary>
public sealed class CompositionPreview : UserControl
{
    private readonly Canvas _canvas = new() { Background = Format.BrushFromHex("#192B30") };
    public CompositionPreview(System.Drawing.Rectangle source, CompositionLayout layout)
    {
        SourceBounds = source; Layout = layout;
        Height = 240; Content = _canvas;
        AutomationProperties.SetName(this, $"Composition preview. {layout.SourceText}. {layout.OutputText}. {layout.ScaleText}");
        SizeChanged += (_, _) => Draw();
    }
    public System.Drawing.Rectangle SourceBounds { get; }
    public CompositionLayout Layout { get; }
    private ImageSource? _source;
    public ImageSource? Snapshot { get => _source; set { _source = value; Draw(); } }

    private void Draw()
    {
        _canvas.Children.Clear();
        var layout = Layout;
        if (!layout.IsAvailable || layout.OutputSize.Width <= 0 || ActualWidth <= 0) return;
        double scale = Math.Min((ActualWidth - 16) / layout.OutputSize.Width, (Height - 16) / layout.OutputSize.Height);
        if (scale <= 0) return;
        double ox = (ActualWidth - layout.OutputSize.Width * scale) / 2, oy = (Height - layout.OutputSize.Height * scale) / 2;
        var output = new Border { Width = layout.OutputSize.Width * scale, Height = layout.OutputSize.Height * scale, Background = Format.BrushFromHex("#000000") };
        Canvas.SetLeft(output, ox); Canvas.SetTop(output, oy); _canvas.Children.Add(output);
        var content = layout.ContentBounds;
        var crop = new Canvas
        {
            Width = content.Width * scale, Height = content.Height * scale,
            Background = Format.BrushFromHex("#31494F"),
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, content.Width * scale, content.Height * scale) }
        };
        Canvas.SetLeft(crop, ox + content.X * scale); Canvas.SetTop(crop, oy + content.Y * scale);
        _canvas.Children.Add(crop);
        if (_source is not null)
        {
            double imageScale = layout.Scale * scale;
            var image = new Image { Source = _source, Stretch = Stretch.Fill, Width = SourceBounds.Width * imageScale, Height = SourceBounds.Height * imageScale };
            Canvas.SetLeft(image, -(layout.Crop.X - SourceBounds.X) * imageScale);
            Canvas.SetTop(image, -(layout.Crop.Y - SourceBounds.Y) * imageScale);
            crop.Children.Add(image);
        }
        else
        {
            var text = new TextBlock { Text = "No screen snapshot", Foreground = Format.BrushFromHex("#D6E5E3"), TextWrapping = TextWrapping.Wrap, Width = Math.Max(1, crop.Width - 12), FontSize = 12 };
            Canvas.SetLeft(text, 6); Canvas.SetTop(text, 8); crop.Children.Add(text);
        }
        if (!layout.SafeArea.IsEmpty)
        {
            double imageScale = layout.Scale * scale;
            var safe = new Shapes.Rectangle
            {
                Width = layout.SafeArea.Width * imageScale, Height = layout.SafeArea.Height * imageScale,
                Stroke = Format.BrushFromHex("#F7B538"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            Canvas.SetLeft(safe, (layout.SafeArea.X - layout.Crop.X) * imageScale);
            Canvas.SetTop(safe, (layout.SafeArea.Y - layout.Crop.Y) * imageScale);
            crop.Children.Add(safe);
        }
    }
}
