using System.ComponentModel;
using ExactFrame.Controls;
using ExactFrame.Core.Tests;
using ExactFrame.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ExactFrame.WindowsChecks;

/// <summary>Exercises the production WinUI preview, which the platform-independent tests cannot cover.</summary>
internal static class PreviewLayoutChecks
{
    public static async Task<bool> Run(List<object> checks)
    {
        using var overlay = new FakeOverlay();
        using var vm = new MainViewModel(new FakeDisplays(), new FakeWindows(), new FakePicker(), overlay,
            new FakeHotkeys(), new FakeClipboard(), new FakeDialogs(), new MemoryStore(), new FakeStartup());
        vm.Initialize();
        var map = new DisplayMap { Editor = vm, State = vm.Preview };
        var host = new Grid { Width = 740, Height = 420, Children = { map } };
        var window = new Window { Title = "ExactFrame preview layout checks", Content = host };
        void BindPreview(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(vm.Preview)) map.State = vm.Preview;
        }
        vm.PropertyChanged += BindPreview;
        bool passed = true;
        void Check(string name, bool pass)
        {
            checks.Add(new { kind = "previewLayout", name, pass });
            passed &= pass;
        }
        try
        {
            window.Activate();
            await Settle(map);
            var canvas = Descendants(map).OfType<Canvas>().Single();
            Check("Preview renders on load", canvas.Children.Count > 0);
            Check("Rebuilt visuals do not receive pointer input", canvas.Children.All(child => !child.IsHitTestVisible));

            // Input must hit the canvas even over the frame label, handles and desktop decoration.
            bool stableInput = true;
            for (int x = 20; x < host.ActualWidth; x += 20)
            for (int y = 20; y < host.ActualHeight; y += 20)
            {
                var point = map.TransformToVisual(host).TransformPoint(new Point(x, y));
                var hit = VisualTreeHelper.FindElementsInHostCoordinates(point, map).FirstOrDefault();
                stableInput &= hit is null || hit == canvas || !Descendants(canvas).Contains(hit);
            }
            Check("Pointer target stays stable across the preview", stableInput);

            var originalVisual = canvas.Children[0];
            vm.BeginFrameInteraction();
            for (int i = 0; i < 40; i++) vm.SetFrameFromInteraction(new(100 + i, 120 + i, 1280, 720));
            Check("Frame notifications defer visual replacement", canvas.Children[0] == originalVisual);
            vm.FinishFrameInteraction();
            await Settle(map);
            Check("Queued redraw renders the final frame", canvas.Children.Count > 0 &&
                canvas.Children[0] != originalVisual && ((PreviewState)map.State!).Frame == vm.CurrentFrame);

            for (int i = 0; i < 40; i++)
            {
                vm.Displays[i % vm.Displays.Count].SelectCommand.Execute(null);
                host.Width = 500 + i % 3 * 80;
                host.Height = 300 + i % 4 * 20;
                vm.NudgeFrame(i % 2 == 0 ? 1 : -1, 1, resize: false);
                await Settle(map);
            }
            Check("Display changes and layout changes settle", canvas.Children.Count > 0 &&
                ((PreviewState)map.State!).Frame == vm.CurrentFrame);
            Check("Redraws preserve stable input", canvas.Children.All(child => !child.IsHitTestVisible));
        }
        finally
        {
            vm.PropertyChanged -= BindPreview;
            window.Close();
        }
        return passed;
    }

    private static async Task Settle(FrameworkElement element)
    {
        await Task.Delay(30);
        element.UpdateLayout();
        await Task.Delay(30);
    }

    private static IEnumerable<UIElement> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is UIElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
