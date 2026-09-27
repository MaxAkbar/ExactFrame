using System.Drawing;

namespace ExactFrame.Core.Models;

/// <summary>Which part of an app window must match the requested size.</summary>
public enum WindowArea
{
    /// <summary>Visible frame and title bar, excluding Windows' invisible resize borders.</summary>
    VisibleFrame,

    /// <summary>The native client rectangle, excluding the standard title bar and frame.</summary>
    Client
}

/// <summary>
/// An open top-level window. Handle, process and thread together identify it safely.
/// For a minimized window, <see cref="Bounds"/> is the size it restores to.
/// </summary>
public sealed record WindowInfo(
    nint Handle, uint ProcessId, uint ThreadId, string Title, string ProcessName, Rectangle Bounds, bool IsMinimized = false)
{
    public bool IsSameWindow(WindowInfo? other) =>
        other is not null && other.Handle == Handle && other.ProcessId == ProcessId && other.ThreadId == ThreadId;
}

/// <param name="Actual">The measured capture rectangle after resizing.</param>
/// <param name="Exact">Whether the app accepted the requested rectangle exactly.</param>
/// <param name="WasRestored">Whether the window had to be brought back from minimized or maximized first.</param>
public sealed record ResizeResult(Rectangle Actual, bool Exact, bool WasRestored = false);

public enum WindowObservationKind
{
    Visible,
    Unavailable,
    Closed
}

/// <summary>The current capture bounds of a tracked app, or why no bounds can be shown.</summary>
public readonly record struct WindowObservation(WindowObservationKind Kind, Rectangle Bounds);
