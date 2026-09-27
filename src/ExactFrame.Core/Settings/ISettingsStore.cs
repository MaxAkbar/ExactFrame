namespace ExactFrame.Core.Settings;

public interface ISettingsStore
{
    /// <summary>Loads settings, or defaults when the file is missing or unreadable.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
