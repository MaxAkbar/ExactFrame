using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

public interface IWindowPicker
{
    /// <summary>Waits for the next click on another app's window. Returns <c>null</c> when canceled.</summary>
    Task<WindowInfo?> PickAsync(CancellationToken cancellation);
}
