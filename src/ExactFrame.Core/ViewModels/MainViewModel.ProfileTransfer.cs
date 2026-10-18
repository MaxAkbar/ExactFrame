using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;

namespace ExactFrame.Core.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ImportProfilesAsync()
    {
        if (_profileTransfer is null) return;
        string? json = await _profileTransfer.ReadImportAsync(_lifetime.Token);
        if (json is null) return;
        var package = ProfilePackageSerializer.Parse(json);
        var displays = _displayService.GetDisplays().ToList();
        var mappings = await _profileTransfer.ReviewImportAsync(package, displays, _lifetime.Token);
        if (mappings is null) return;
        _lifetime.Token.ThrowIfCancellationRequested();

        // Re-read after the dialog: a display may have disconnected while the user reviewed it.
        var result = ProfilePackageSerializer.Remap(package, mappings, _displayService.GetDisplays());
        var names = _settings.Profiles.Select(p => p.Name).ToList();
        var copies = new List<FrameProfile>();
        foreach (var profile in result.Profiles)
        {
            string name = UniqueProfileName(profile.Name, names);
            names.Add(name);
            copies.Add(profile with { Name = name });
        }
        if (!TrySaveProfiles([.. _settings.Profiles, .. copies], _settings.ActiveProfileId)) return;
        string count = copies.Count == 1 ? "1 profile" : $"{copies.Count} profiles";
        string warning = result.Warnings.Count > 0 ? $" {result.Warnings[0]}" : string.Empty;
        SetStatus($"Imported {count} as new copies. Choose Apply when ready.{warning}", warning: result.Warnings.Count > 0);
    }

    private async Task ExportProfilesAsync(FrameProfile? profile)
    {
        if (_profileTransfer is null) return;
        IReadOnlyList<FrameProfile> profiles = profile is null ? _settings.Profiles : [profile];
        if (profiles.Count == 0) return;
        string json = ProfilePackageSerializer.Export(profiles, _displayService.GetDisplays());
        string name = profile?.Name ?? "ExactFrame profiles";
        if (await _profileTransfer.WriteExportAsync(name, json, _lifetime.Token))
            SetStatus(profile is null ? $"Exported {profiles.Count} profiles." : $"Exported “{profile.Name}”.");
    }

    private static string UniqueProfileName(string name, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string basis = name[..Math.Min(name.Length, 60)];
        if (!names.Contains(basis)) return basis;
        for (int number = 2; ; number++)
        {
            string suffix = $" ({number})";
            string candidate = basis[..Math.Min(basis.Length, 60 - suffix.Length)] + suffix;
            if (!names.Contains(candidate)) return candidate;
        }
    }
}
