using System.Drawing;

namespace ExactFrame.Core.Services;

public interface ISnapStudioCaptureService
{
    Task<string?> ChooseApplicationAsync(CancellationToken cancellation);
    Task CaptureAsync(string executable, Rectangle region, string name, CancellationToken cancellation);
}
