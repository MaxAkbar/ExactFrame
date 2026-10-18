using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

public interface IOutputPreviewService
{
    Task ShowAsync(Rectangle source, IReadOnlyList<NestedFrame> frames, CancellationToken cancellation = default);
}
