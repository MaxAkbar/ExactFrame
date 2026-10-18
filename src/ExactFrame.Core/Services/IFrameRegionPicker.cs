using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

public interface IFrameRegionPicker
{
    /// <summary>
    /// Lets the user draw a frame in physical screen pixels on one of the supplied displays.
    /// Returns <c>null</c> after cancellation or a selection smaller than 100 pixels on either axis.
    /// </summary>
    Task<Rectangle?> PickAsync(IReadOnlyList<DisplayInfo> displays, bool keepClearOfTaskbar,
        CancellationToken cancellation);
}
