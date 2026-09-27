using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

/// <summary>Finds, measures, resizes and restores other apps' windows.</summary>
public interface IWindowService
{
    /// <summary>Raised on the UI thread when the tracked window's capture bounds or visibility change.</summary>
    event EventHandler<WindowObservation>? TrackedWindowChanged;

    IReadOnlyList<WindowInfo> GetWindows();

    /// <summary>Measures a window's capture rectangle in physical pixels.</summary>
    Rectangle Measure(WindowInfo window, WindowArea area);

    bool CanRestore(WindowInfo? window);

    Task<ResizeResult> ResizeAsync(WindowInfo window, Rectangle desired, WindowArea area, CancellationToken cancellation);

    Task RestoreAsync(WindowInfo window, CancellationToken cancellation);

    /// <summary>Starts following one window; a previous tracked window is replaced.</summary>
    void StartTracking(WindowInfo window, WindowArea area);

    void StopTracking();
}
