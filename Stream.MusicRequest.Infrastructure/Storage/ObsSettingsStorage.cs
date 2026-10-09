using System.Text.Json;

namespace Stream.MusicRequest.Infrastructure.Storage;

public class ObsSettingsStorage
{
    private const string FileName =
        "obs.json";

    private readonly string _filePath;

    public ObsSettingsStorage(
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

    public ObsSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new ObsSettings();
        }

        try
        {
            var json =
                File.ReadAllText(_filePath);

            var settings =
                JsonSerializer.Deserialize<ObsSettings>(
                    json);

            return settings
                ?? new ObsSettings();
        }
        catch
        {
            return new ObsSettings();
        }
    }

    public async Task SaveAsync(
        ObsSettings settings)
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

public class ObsSettings
{
    public string BindAddress { get; set; } =
        "127.0.0.1";

    public int WidgetPort { get; set; } =
        8765;

    public int AudioPort { get; set; } =
        8766;

    public double ScrollSpeed { get; set; } =
        60.0;

    public string DisplayMode { get; set; } =
        "CoverLeft";
}

public class PlaybackSettingsStorage
{
    private const string FileName =
        "playback.json";

    private readonly string _filePath;

    public PlaybackSettingsStorage(
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

    public PlaybackSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new PlaybackSettings();
        }

        try
        {
            var json =
                File.ReadAllText(_filePath);

            var settings =
                JsonSerializer.Deserialize<PlaybackSettings>(
                    json);

            return settings
                ?? new PlaybackSettings();
        }
        catch
        {
            return new PlaybackSettings();
        }
    }

    public async Task SaveAsync(
        PlaybackSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(
                nameof(settings));
        }

        settings.Volume =
            Math.Clamp(
                settings.Volume,
                0.0f,
                1.0f);

        settings.DefaultPlaylistUrl =
            settings.DefaultPlaylistUrl?.Trim()
            ?? string.Empty;

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

public class PlaybackSettings
{
    public float Volume { get; set; } =
        0.75f;

    public string DefaultPlaylistUrl { get; set; } =
        string.Empty;

    public bool LoadDefaultPlaylistOnStartup { get; set; } =
        false;

    // "Playlist" is the backward-compatible default for existing playback.json files.
    public string MainMusicSource { get; set; } =
        "Playlist";
}
