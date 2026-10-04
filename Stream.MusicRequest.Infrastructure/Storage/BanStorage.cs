using System.Text.Json;
using Stream.MusicRequest.Core.Infrastructure.Storage;
using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Infrastructure.Storage;

public class BanStorage : IBanStorage
{
    private const string FileName =
        "bans.json";

    private readonly string _filePath;

    public BanStorage(
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

    public BanStorageModel Load()
    {
        if (!File.Exists(_filePath))
        {
            return new BanStorageModel();
        }

        try
        {
            var json =
                File.ReadAllText(_filePath);

            var data =
                JsonSerializer.Deserialize<BanStorageModel>(
                    json);

            return data
                ?? new BanStorageModel();
        }
        catch
        {
            return new BanStorageModel();
        }
    }

    public async Task SaveAsync(
        BanStorageModel data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(
                nameof(data));
        }

        var json =
            JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        await File.WriteAllTextAsync(
            _filePath,
            json);
    }
}
