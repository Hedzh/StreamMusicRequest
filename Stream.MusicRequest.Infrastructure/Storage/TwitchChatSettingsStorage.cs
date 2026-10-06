using System.Text.Json;
using Stream.MusicRequest.Twitch.Configuration;

namespace Stream.MusicRequest.Infrastructure.Storage;

public class TwitchChatSettingsStorage
{
    private const string FileName = "twitch-chat-settings.json";
    private readonly string _filePath;

    public TwitchChatSettingsStorage(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    public async Task<TwitchChatSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new TwitchChatSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<TwitchChatSettings>(json)
                   ?? new TwitchChatSettings();
        }
        catch
        {
            return new TwitchChatSettings();
        }
    }

    public async Task SaveAsync(TwitchChatSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        var json = JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(_filePath, json);
    }
}
