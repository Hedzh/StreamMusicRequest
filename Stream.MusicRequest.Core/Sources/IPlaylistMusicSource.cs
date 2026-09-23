using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public interface IPlaylistMusicSource
{
    string Name { get; }

    bool CanHandlePlaylistUrl(
        string url);

    Task<MusicAlbum?> ResolvePlaylistAsync(
        string url);
}