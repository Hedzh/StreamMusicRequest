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
}
