using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public interface IAlbumMusicSource
{
    string Name { get; }

    bool CanHandleAlbumUrl(
        string url);

    Task<MusicAlbum?> ResolveAlbumAsync(
        string url);
}