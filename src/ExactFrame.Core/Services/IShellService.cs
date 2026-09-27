namespace ExactFrame.Core.Services;

public interface IShellService
{
    /// <summary>Opens File Explorer at a file, or at its folder when the file doesn't exist yet.</summary>
    void RevealInExplorer(string path);
}
