using System.Drawing;
using System.Drawing.Imaging;
using ExactFrame.Controls;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ExactFrame.Services;

/// <summary>Local composition previews; screen pixels are captured only on an explicit refresh and never saved.</summary>
internal sealed class OutputPreviewService(IDisplayService displays) : IOutputPreviewService
{
    private MainWindow? _window;
    public void Attach(MainWindow window) => _window = window;

    public async Task ShowAsync(Rectangle source, IReadOnlyList<NestedFrame> frames, CancellationToken cancellation = default)
    {
        if (_window?.Content?.XamlRoot is not { } root) return;
        var window = _window;
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var previewToken = closed.Token;
        var originalDisplays = displays.GetDisplays().ToArray();
        var previews = new List<CompositionPreview>();
        var content = new StackPanel { Spacing = 14, Width = 460 };
        content.Children.Add(Caption("Compare crops and output scaling. Previews fit this window. Safe margins are composition guides. These settings do not change recorder output."));
        var refresh = new Button { Content = "Refresh from screen", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(refresh, "Refresh composition previews from visible screen");
        var status = Caption("No snapshot yet. Refresh captures the visible main frame while ExactFrame briefly hides. Images stay in memory until this window closes.");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        content.Children.Add(refresh); content.Children.Add(status);
        foreach (var frame in frames)
        {
            var layout = CompositionLayout.Create(source, frame);
            var preview = new CompositionPreview(source, layout);
            previews.Add(preview);
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(new TextBlock { Text = frame.Name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(Caption(layout.SourceText + " → " + layout.OutputText));
            card.Children.Add(Caption(layout.ScaleText));
            card.Children.Add(Caption(frame.SafeMarginPercent > 0 ? $"Safe margin: {frame.SafeMarginPercent}% on each edge of the crop." : "Safe-margin guide off."));
            card.Children.Add(preview);
            content.Children.Add(new Border { Padding = new Thickness(12), BorderThickness = new Thickness(1), BorderBrush = Format.ThemeBrush("LineBrush"), CornerRadius = new CornerRadius(8), Child = card });
        }
        var dialog = new ContentDialog
        {
            Title = "Output compositions", CloseButtonText = "Close", XamlRoot = root,
            Content = new ScrollViewer { Content = content, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };
        AppThemeHelper.MatchOwner(dialog);
        dialog.Closed += (_, _) => closed.Cancel();
        bool capturing = false;
        refresh.Click += async (_, _) =>
        {
            if (capturing || previewToken.IsCancellationRequested) return;
            capturing = true; refresh.IsEnabled = false; dialog.IsEnabled = false;
            bool hidden = false;
            try
            {
                if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > 16_777_216 ||
                    !displays.GetDisplays().Any(display => display.Bounds.Contains(source)))
                    throw new InvalidOperationException("The main frame must fit on one connected display and contain at most 16 million pixels for a screen preview.");
                foreach (var preview in previews) preview.Snapshot = null;
                window.AppWindow.Hide(); hidden = true;
                await Task.Delay(400, previewToken);
                if (!originalDisplays.SequenceEqual(displays.GetDisplays()))
                    throw new InvalidOperationException("Display settings changed. Close and reopen these previews before refreshing.");
                byte[] pixels = await Task.Run(() => Capture(source, previewToken), previewToken);
                previewToken.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(pixels);
                var image = new BitmapImage();
                await image.SetSourceAsync(stream.AsRandomAccessStream());
                previewToken.ThrowIfCancellationRequested();
                foreach (var preview in previews) preview.Snapshot = image;
                status.Text = $"Screen snapshot refreshed at {DateTime.Now:t}. Check text and controls in each crop. Recorded output remains unverified.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or IOException or ArgumentException)
            { status.Text = $"Screen preview unavailable: {ex.Message}"; }
            finally
            {
                if (hidden && !cancellation.IsCancellationRequested) { window.AppWindow.Show(); window.Activate(); }
                capturing = false; refresh.IsEnabled = true; dialog.IsEnabled = true;
            }
        };
        try { await dialog.ShowAsync(); }
        finally { closed.Cancel(); foreach (var preview in previews) preview.Snapshot = null; }
    }

    private static byte[] Capture(Rectangle source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var image = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
            graphics.CopyFromScreen(source.X, source.Y, 0, 0, source.Size, CopyPixelOperation.SourceCopy);
        cancellation.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Format.ThemeBrush("MutedBrush") };
}
