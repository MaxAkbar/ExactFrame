using CSharpDB.Engine;

namespace ExactFrame.Core.Settings;

/// <summary>
/// Owns ExactFrame's embedded database. Settings are one typed document; other features can use
/// separate typed collections through <see cref="IAppDataStore"/>.
/// </summary>
public sealed class CSharpDbAppStore(string filePath, string? legacySettingsPath = null)
    : ISettingsStore, IAppDataStore, IDisposable, IAsyncDisposable
{
    private const string SettingsCollection = "app_settings";
    private const string SettingsKey = "current";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Database? _database;
    private bool _disposed;

    public string FilePath { get; } = filePath;

    public string? LegacySettingsPath { get; } = legacySettingsPath;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExactFrame", "exactframe.db");

    // The view model's existing settings contract is synchronous. Run the async database work on a
    // worker thread so it cannot capture the WinUI synchronization context while the caller waits.
    public AppSettings Load() => Task.Run(LoadSettingsAsync).GetAwaiter().GetResult();

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Task.Run(() => PutAsync(SettingsCollection, SettingsKey, settings)).GetAwaiter().GetResult();
    }

    public async Task<T?> GetAsync<T>(string collection, string key, CancellationToken cancellationToken = default)
    {
        ValidateAddress(collection, key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var documents = await (await OpenAsync(cancellationToken).ConfigureAwait(false))
                .GetCollectionAsync<T>(collection, cancellationToken).ConfigureAwait(false);
            return await documents.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PutAsync<T>(string collection, string key, T value, CancellationToken cancellationToken = default)
    {
        ValidateAddress(collection, key);
        ArgumentNullException.ThrowIfNull(value);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var documents = await (await OpenAsync(cancellationToken).ConfigureAwait(false))
                .GetCollectionAsync<T>(collection, cancellationToken).ConfigureAwait(false);
            await documents.PutAsync(key, value, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync<T>(string collection, string key, CancellationToken cancellationToken = default)
    {
        ValidateAddress(collection, key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var documents = await (await OpenAsync(cancellationToken).ConfigureAwait(false))
                .GetCollectionAsync<T>(collection, cancellationToken).ConfigureAwait(false);
            return await documents.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_database is not null) await _database.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();

    private async Task<AppSettings> LoadSettingsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var documents = await (await OpenAsync(CancellationToken.None).ConfigureAwait(false))
                .GetCollectionAsync<AppSettings>(SettingsCollection).ConfigureAwait(false);
            var saved = await documents.GetAsync(SettingsKey).ConfigureAwait(false);
            if (saved is not null) return saved;

            // Import once. Keep the old file untouched as a recovery copy; later loads use the database.
            var settings = LegacySettingsPath is { } path && File.Exists(path)
                ? new JsonSettingsStore(path).Load()
                : new AppSettings();
            await documents.PutAsync(SettingsKey, settings).ConfigureAwait(false);
            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<Database> OpenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_database is not null) return _database;
        string? folder = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        _database = await Database.OpenAsync(FilePath, cancellationToken).ConfigureAwait(false);
        return _database;
    }

    private static void ValidateAddress(string collection, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }
}
