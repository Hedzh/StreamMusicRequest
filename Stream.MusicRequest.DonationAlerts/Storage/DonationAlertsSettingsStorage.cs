using System.Text.Json;
using Stream.MusicRequest.DonationAlerts.Models;

namespace Stream.MusicRequest.DonationAlerts.Storage;

public sealed class DonationAlertsSettingsStorage
{
    private const string FileName = "donationalerts.json";

    private readonly string _filePath;

    public DonationAlertsSettingsStorage()
    {
        var directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "Settings");

        Directory.CreateDirectory(directory);

        _filePath =
            Path.Combine(directory, FileName);
    }

    public DonationAlertsSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new DonationAlertsSettings();
        }

        try
        {
            var json =
                File.ReadAllText(_filePath);

            var settings =
                JsonSerializer.Deserialize<DonationAlertsSettings>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            return settings ??
                   new DonationAlertsSettings();
        }
        catch
        {
            return new DonationAlertsSettings();
        }
    }

    public async Task SaveAsync(
        DonationAlertsSettings settings)
    {
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
