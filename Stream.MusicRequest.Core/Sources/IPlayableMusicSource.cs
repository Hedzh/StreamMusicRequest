using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public interface IPlayableMusicSource : IMusicSource
{
    Task<string?> GetPlaybackUrlAsync(
        Track track);
}