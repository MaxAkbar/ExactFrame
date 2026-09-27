using System.Drawing;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Tests;

public sealed class TitleBarScanTests
{
    // A restored browser window at 100% scaling. The columns sit in the caption buttons and over the first tab.
    private static readonly Rectangle Client = new(100, 50, 1280, 800);
    private const int RightColumn = 1359;
    private const int LeftColumn = 120;
    private static readonly int[] Columns = [RightColumn, LeftColumn];

    private static int? Scan(Func<int, int, WindowPart> partAt, Rectangle? client = null, int[]? columns = null)
    {
        Assert.True(TitleBarScan.TryFindContentTop(client ?? Client, columns ?? Columns, 128, partAt, out int? top));
        return top;
    }

    [Fact]
    public void Tab_strip_drawn_inside_the_client_area_is_left_out()
    {
        // A 4 px top resize edge, then the tab strip: caption buttons on the right, a tab (content) on the left.
        int? top = Scan((x, y) =>
            y < Client.Top + 4 ? WindowPart.TitleBar
            : x == RightColumn && y < Client.Top + 41 ? WindowPart.TitleBar
            : WindowPart.Content);

        Assert.Equal(Client.Top + 41, top);
    }

    [Fact]
    public void Standard_title_bar_outside_the_client_area_changes_nothing()
    {
        int probes = 0;
        int? top = Scan((_, _) =>
        {
            probes++;
            return WindowPart.Content;
        });

        Assert.Null(top);
        Assert.Equal(Columns.Length, probes);
    }

    [Fact]
    public void Caption_buttons_on_the_left_are_found_in_right_to_left_layouts()
    {
        int? top = Scan((x, y) => x == LeftColumn && y < Client.Top + 32 ? WindowPart.TitleBar : WindowPart.Content);

        Assert.Equal(Client.Top + 32, top);
    }

    [Fact]
    public void Title_bar_ends_at_the_first_part_that_is_not_title_bar()
    {
        int? top = Scan((_, y) => y < Client.Top + 30 ? WindowPart.TitleBar : WindowPart.Other);

        Assert.Equal(Client.Top + 30, top);
    }

    [Fact]
    public void Side_border_at_the_top_is_not_a_title_bar()
    {
        Assert.Null(Scan((_, _) => WindowPart.Other));
    }

    [Fact]
    public void Window_that_stops_answering_gives_no_result()
    {
        bool answered = TitleBarScan.TryFindContentTop(Client, Columns, 128,
            (_, y) => y < Client.Top + 10 ? WindowPart.TitleBar : WindowPart.Unknown, out int? top);

        Assert.False(answered);
        Assert.Null(top);
    }

    [Fact]
    public void Window_you_can_drag_from_anywhere_is_not_trimmed()
    {
        int deepest = int.MinValue;
        int? top = Scan((_, y) =>
        {
            deepest = Math.Max(deepest, y);
            return WindowPart.TitleBar;
        });

        Assert.Null(top);
        Assert.Equal(Client.Top + 127, deepest);
    }

    [Fact]
    public void Scan_stays_inside_a_short_client_area()
    {
        var shortClient = new Rectangle(0, -600, 400, 20);
        int deepest = int.MinValue;
        int? top = Scan((_, y) =>
        {
            deepest = Math.Max(deepest, y);
            return WindowPart.TitleBar;
        }, shortClient, [390, 10]);

        Assert.Null(top);
        Assert.Equal(shortClient.Bottom - 1, deepest);
    }
}
