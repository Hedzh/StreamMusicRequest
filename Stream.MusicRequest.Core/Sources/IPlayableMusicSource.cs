using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public interface IPlayableMusicSource : IMusicSource
{
    bool CanHandleTrackUrl(
        string url);

    Task<string?> GetPlaybackUrlAsync(
        Track track);
}