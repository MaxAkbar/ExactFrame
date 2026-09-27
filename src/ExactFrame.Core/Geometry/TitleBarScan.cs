using System.Drawing;

namespace ExactFrame.Core.Geometry;

/// <summary>What a window says is at a screen point (its answer to WM_NCHITTEST), simplified.</summary>
public enum WindowPart
{
    /// <summary>The app's content (HTCLIENT).</summary>
    Content,

    /// <summary>Title bar: the caption, a caption button, the system menu or the top resize edge.</summary>
    TitleBar,

    /// <summary>Anything else, such as a side border.</summary>
    Other,

    /// <summary>The window didn't answer in time.</summary>
    Unknown
}

/// <summary>
/// Finds where a title bar that an app draws itself ends. Chrome, Edge, VS Code and Windows Terminal draw their
/// tabs or title inside the native client area, so the client rectangle alone includes the title bar.
/// </summary>
public static class TitleBarScan
{
    /// <summary>
    /// Walks down each column from the top of <paramref name="client"/> to find the first row below an
    /// app-drawn title bar. With several columns, the lowest row wins, so caption buttons on either side count.
    /// </summary>
    /// <param name="contentTop">
    /// The first content row, or <c>null</c> when there's no app-drawn title bar: no column starts with one (apps
    /// with the standard Windows title bar, which sits outside the client area), or a column is still title bar at
    /// <paramref name="maxDepth"/>, as in a window you can drag from anywhere.
    /// </param>
    /// <returns><c>false</c> when the window stopped answering, so the result is unknown.</returns>
    public static bool TryFindContentTop(Rectangle client, IReadOnlyList<int> columns, int maxDepth,
        Func<int, int, WindowPart> partAt, out int? contentTop)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(partAt);

        contentTop = null;
        int bottom = client.Top + Math.Min(maxDepth, client.Height);
        int? lowest = null;
        foreach (int x in columns)
        {
            bool inTitleBar = false;
            int y = client.Top;
            for (; y < bottom; y++)
            {
                var part = partAt(x, y);
                if (part == WindowPart.Unknown) return false;
                if (part != WindowPart.TitleBar) break;
                inTitleBar = true;
            }

            if (!inTitleBar) continue;
            if (y >= bottom) return true;
            lowest = Math.Max(lowest ?? y, y);
        }

        contentTop = lowest;
        return true;
    }
}
