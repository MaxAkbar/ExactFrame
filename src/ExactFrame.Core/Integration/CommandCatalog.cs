using System.Text.Json;

namespace ExactFrame.Core.Integration;

public sealed record CatalogProfile(string Id, string Name, string Description);
public sealed record CommandCatalog(int Version, string ExecutablePath, IReadOnlyList<CatalogProfile> Profiles)
{
    public const int CurrentVersion = 1;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExactFrame", "command-palette.json");

    public static CommandCatalog? Read(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The ExactFrame command catalog is too large.");
        var catalog = JsonSerializer.Deserialize<CommandCatalog>(File.ReadAllText(path));
        if (catalog is null || catalog.Version != CurrentVersion || catalog.Profiles is null || catalog.Profiles.Count > 1000 ||
            string.IsNullOrWhiteSpace(catalog.ExecutablePath) || !Path.IsPathFullyQualified(catalog.ExecutablePath) ||
            !string.Equals(Path.GetFileName(catalog.ExecutablePath), "ExactFrame.exe", StringComparison.OrdinalIgnoreCase) ||
            catalog.Profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 256 ||
                string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 || p.Description is null))
            throw new InvalidDataException("Open ExactFrame and refresh Command Palette sharing.");
        return catalog;
    }
}

public interface ICommandCatalogPublisher
{
    void Publish(IReadOnlyList<CatalogProfile> profiles);
    void Disable();
}

/// <summary>Only called after explicit opt-in. Never opens the application's database in the companion.</summary>
public sealed class CommandCatalogPublisher(string path, string executablePath) : ICommandCatalogPublisher
{
    public void Publish(IReadOnlyList<CatalogProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CommandCatalog(CommandCatalog.CurrentVersion, executablePath, profiles)));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Disable() => File.Delete(path);
}
