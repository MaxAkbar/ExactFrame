using ExactFrame.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ExactFrame.Services;

internal sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
