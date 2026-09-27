using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

public interface IDisplayService
{
    /// <summary>Raised on the UI thread when monitors are added, removed or rearranged.</summary>
    event EventHandler? DisplaysChanged;

    IReadOnlyList<DisplayInfo> GetDisplays();
}
