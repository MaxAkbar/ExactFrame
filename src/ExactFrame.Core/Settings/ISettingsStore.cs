namespace ExactFrame.Core.Settings;

public interface ISettingsStore
{
    /// <summary>Loads saved settings, or defaults when no settings have been stored.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
