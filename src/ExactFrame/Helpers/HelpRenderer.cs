using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ExactFrame.Core.Help;

namespace ExactFrame.Helpers;

/// <summary>Turns help topics (plain data in Core) into WinUI elements. Text is selectable.</summary>
internal static class HelpRenderer
{
    private const double BodySize = 14;
    private const double LineHeight = 22;

    public static void Render(Panel host, HelpTopic topic)
    {
        host.Children.Clear();
        foreach (var section in topic.Sections)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = section.Heading,
                FontSize = 16,
                FontFamily = Font("SansSemiBoldFont"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("InkBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var block in section.Blocks) panel.Children.Add(RenderBlock(block));
            host.Children.Add(panel);
        }
    }

    private static UIElement RenderBlock(HelpBlock block) => block.Kind switch
    {
        HelpBlockKind.Steps => List(block.Items, numbered: true),
        HelpBlockKind.Bullets => List(block.Items, numbered: false),
        HelpBlockKind.Tip => Tip(block.Text),
        _ => Text(block.Text)
    };

    private static RichTextBlock Text(string text, Brush? foreground = null)
    {
        var paragraph = new Paragraph();
        foreach (var inline in Inlines(text)) paragraph.Inlines.Add(inline);
        var block = new RichTextBlock
        {
            FontSize = BodySize,
            LineHeight = LineHeight,
            FontFamily = Font("SansFont"),
            Foreground = foreground ?? Brush("InkBrush"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        block.Blocks.Add(paragraph);
        return block;
    }

    /// <summary>Splits on <c>**</c>: odd segments are UI labels, shown semibold.</summary>
    private static IEnumerable<Inline> Inlines(string text)
    {
        string[] parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run { Text = parts[i] };
            if (i % 2 == 0)
            {
                yield return run;
                continue;
            }
            var label = new Span { FontFamily = Font("SansSemiBoldFont"), FontWeight = FontWeights.SemiBold };
            label.Inlines.Add(run);
            yield return label;
        }
    }

    private static Grid List(IReadOnlyList<string> items, bool numbered)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement marker = numbered ? Number(i + 1) : Dot();
            Grid.SetRow(marker, i);
            grid.Children.Add(marker);

            var text = Text(items[i]);
            Grid.SetRow(text, i);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static Border Number(int value) => new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        Background = Brush("AccentTintBrush"),
        VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock
        {
            Text = value.ToString(),
            FontSize = 12,
            FontFamily = Font("SansSemiBoldFont"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("AccentStrongBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private static Border Dot() => new()
    {
        Width = 6,
        Height = 6,
        Margin = new Thickness(8, 8, 0, 0),
        CornerRadius = new CornerRadius(3),
        Background = Brush("AccentBrush"),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top
    };

    private static Border Tip(string text)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon
        {
            Glyph = "",
            FontSize = 16,
            Foreground = Brush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0)
        });
        var body = Text(text, Brush("InkSoftBrush"));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        return new Border
        {
            Padding = new Thickness(14, 12, 16, 12),
            CornerRadius = new CornerRadius(10),
            Background = Brush("AccentSoftBrush"),
            BorderBrush = Brush("AccentLineBrush"),
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    private static FontFamily Font(string key) => (FontFamily)Application.Current.Resources[key];

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
