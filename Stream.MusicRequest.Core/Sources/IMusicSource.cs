using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public interface IMusicSource
{
    string Name { get; }

    bool CanHandleUrl(string url);

    Task<Track?> ResolveTrackAsync(string url);
}
