using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Infrastructure.Storage;

public interface IBanStorage
{
    BanStorageModel Load();

    Task SaveAsync(BanStorageModel data);
}
