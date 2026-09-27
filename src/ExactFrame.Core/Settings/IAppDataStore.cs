namespace ExactFrame.Core.Settings;

/// <summary>Typed, keyed application data stored in the shared CSharpDB database.</summary>
public interface IAppDataStore
{
    Task<T?> GetAsync<T>(string collection, string key, CancellationToken cancellationToken = default);

    Task PutAsync<T>(string collection, string key, T value, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync<T>(string collection, string key, CancellationToken cancellationToken = default);
}
