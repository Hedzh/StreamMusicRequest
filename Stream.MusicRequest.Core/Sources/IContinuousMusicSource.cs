using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

/// <summary>Contract for a main music source that supplies tracks continuously.</summary>
public interface IContinuousMusicSource
{
    Task<int> AppendNextBatchAsync(MusicAlbum album);
    Task TrackFinishedAsync(Track track, TimeSpan played);
    Task TrackSkippedAsync(Track track, TimeSpan played);
}
