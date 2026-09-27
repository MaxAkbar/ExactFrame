using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

/// <summary>The on-screen recording outline: border, dimming and guides.</summary>
public interface IOutlineOverlay : IDisposable
{
    /// <summary>Raised on the UI thread while the user drags the border.</summary>
    event EventHandler? FrameMoved;

    /// <summary>Raised when the outline is shown or hidden.</summary>
    event EventHandler? VisibilityChanged;

    bool IsVisible { get; }

    Rectangle Frame { get; }

    /// <summary>The display the outline is on, while visible.</summary>
    DisplayInfo? Display { get; }

    /// <summary>Whether Windows accepted the capture-exclusion request for every overlay window.</summary>
    bool CaptureExclusionApplied { get; }

    /// <summary>Shows the outline, or moves it when already visible. Dragging stays inside the usable area.</summary>
    void Show(Rectangle frame, DisplayInfo display, bool keepClearOfTaskbar, IReadOnlyList<NestedFrameBounds> nestedFrames);

    void Hide();

    void ApplyStyle(OutlineStyle style);

    /// <summary>When locked, clicks pass through the border too.</summary>
    void SetLocked(bool locked);

    void SetCaptureExclusion(bool exclude);
}
