using System.Text.Json;
using Stream.MusicRequest.Core.Infrastructure.Storage;
using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Storage;

public class TwitchAuthStorage
{
    private const string StorageKey =
        "twitch-auth";

    private readonly IProtectedStorage _storage;

    public TwitchAuthStorage(
        IProtectedStorage storage)
    {
        _storage = storage;
    }

    public async Task SaveAsync(
        TwitchAuthSnapshot snapshot)
    {
        var json =
            JsonSerializer.Serialize(snapshot);

        await _storage.SaveAsync(
            StorageKey,
            json);
    }

    public async Task<TwitchAuthSnapshot?> LoadAsync()
    {
        var json =
            await _storage.LoadAsync(
                StorageKey);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<TwitchAuthSnapshot>(
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