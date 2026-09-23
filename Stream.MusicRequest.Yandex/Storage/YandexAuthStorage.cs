using System.Text.Json;
using Stream.MusicRequest.Core.Infrastructure.Storage;
using YandexMusic.Authentication;

namespace Stream.MusicRequest.Yandex.Storage;

public class YandexAuthStorage
{
    private readonly IProtectedStorage _storage;

    private const string StorageKey =
        "yandex-music-auth";

    public YandexAuthStorage(
        IProtectedStorage storage)
    {
        _storage = storage;
    }

    public async Task SaveAsync(
        AuthSnapshot snapshot)
    {
        var json =
            JsonSerializer.Serialize(snapshot);

        await _storage.SaveAsync(
            StorageKey,
            json);
    }

    public async Task<AuthSnapshot?> LoadAsync()
    {
        var json =
            await _storage.LoadAsync(
                StorageKey);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AuthSnapshot>(
            json);
    }

    public Task DeleteAsync()
    {
        return _storage.DeleteAsync(
            StorageKey);
    }

    public Task<bool> ExistsAsync()
    {
        return _storage.ExistsAsync(
            StorageKey);
    }
}