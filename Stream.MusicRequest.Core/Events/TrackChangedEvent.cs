using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Events;

public class TrackChangedEvent
{
    public Track? PreviousTrack { get; }

    public Track CurrentTrack { get; }

    public string ViewerName { get; }

    public TrackChangedEvent(
        Track? previousTrack,
        Track currentTrack,
        string viewerName)
    {
        PreviousTrack = previousTrack;
        CurrentTrack = currentTrack;
        ViewerName = viewerName;
    }
}
