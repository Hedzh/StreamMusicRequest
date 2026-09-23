using System.Text.Json;

namespace Stream.MusicRequest.Infrastructure.Storage;

public class TwitchRewardSettingsStorage
{
    private const string FileName =
        "twitch-rewards.json";

    private readonly string _filePath;

    public TwitchRewardSettingsStorage(
        string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                storageDirectory))
        {
            throw new ArgumentException(
                "Путь к каталогу настроек не указан.",
                nameof(storageDirectory));
        }

        Directory.CreateDirectory(
            storageDirectory);

        _filePath =
            Path.Combine(
                storageDirectory,
                FileName);
    }

    public async Task<TwitchRewardSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new TwitchRewardSettings();
        }

        try
        {
            var json =
                await File.ReadAllTextAsync(
                    _filePath);

            var settings =
                JsonSerializer.Deserialize<TwitchRewardSettings>(
                    json);

            return settings
                ?? new TwitchRewardSettings();
        }
        catch
        {
            return new TwitchRewardSettings();
        }
    }

    public async Task SaveAsync(
        TwitchRewardSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(
                nameof(settings));
        }

        var json =
            JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        await File.WriteAllTextAsync(
            _filePath,
            json);
    }
}

public class TwitchRewardSettings
{
    public string OrderMusicRewardId { get; set; } =
        string.Empty;

    public string SkipTrackRewardId { get; set; } =
        string.Empty;
}