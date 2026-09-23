namespace Stream.MusicRequest.Core.Infrastructure.Storage;

public interface IProtectedStorage
{
    Task SaveAsync(
        string key,
        string value);

    Task<string?> LoadAsync(
        string key);

    Task DeleteAsync(
        string key);

    Task<bool> ExistsAsync(
        string key);
}