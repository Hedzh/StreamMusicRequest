using Stream.MusicRequest.Core.Configuration;

namespace Stream.MusicRequest.Core.Infrastructure.Storage;

public interface ISettingsStorage
{
    Task<AppSettings> LoadAsync();

    Task SaveAsync(AppSettings settings);
}
