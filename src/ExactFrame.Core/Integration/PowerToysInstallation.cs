namespace ExactFrame.Core.Integration;

public sealed record PowerToysInstallation(string Directory, string? FancyZonesCli, string? AwakeExecutable)
{
    public static PowerToysInstallation? Find(IEnumerable<string> roots)
    {
        foreach (string root in roots.Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(root, "PowerToys.exe"))) continue;
            string? cli = FirstExisting(root, "bin/PowerToys.FancyZones.CLI.exe", "FancyZonesCLI.exe");
            string? awake = FirstExisting(root, "PowerToys.Awake.exe");
            return new(root, cli, awake);
        }
        return null;
    }

    private static string? FirstExisting(string root, params string[] files) =>
        files.Select(file => Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(File.Exists);
}
