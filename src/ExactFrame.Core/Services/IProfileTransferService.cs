using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;

namespace ExactFrame.Core.Services;

/// <summary>User-selected profile package files and display mapping before an import.</summary>
public interface IProfileTransferService
{
    /// <summary>Returns UTF-8 package text, or null when the file picker is canceled.</summary>
    Task<string?> ReadImportAsync(CancellationToken cancellation = default);

    /// <summary>Writes a package to the user's chosen destination. False means canceled.</summary>
    Task<bool> WriteExportAsync(string suggestedName, string json, CancellationToken cancellation = default);

    /// <summary>Maps every saved display key to a connected display, or returns null on cancellation.</summary>
    Task<IReadOnlyDictionary<string, string>?> ReviewImportAsync(ProfilePackage package,
        IReadOnlyList<DisplayInfo> displays, CancellationToken cancellation = default);
}
