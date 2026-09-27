using System.ComponentModel;
using System.Diagnostics;
using ExactFrame.Core.Services;

namespace ExactFrame.Services;

internal sealed class ShellService : IShellService
{
    public void RevealInExplorer(string path)
    {
        try
        {
            string arguments;
            if (File.Exists(path))
            {
                arguments = $"/select,\"{path}\"";
            }
            else
            {
                // Settings are only written after the first change, so the file may not exist yet.
                string folder = Path.GetDirectoryName(path) ?? path;
                Directory.CreateDirectory(folder);
                arguments = $"\"{folder}\"";
            }

            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't open File Explorer: {ex.Message}");
        }
    }
}
