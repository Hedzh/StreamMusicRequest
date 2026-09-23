using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Events;

public class TrackStartedEvent
{
    public Track Track { get; }

    public string ViewerName { get; }

    public TrackStartedEvent(Track track, string viewerName)
    {
        Track = track;
        ViewerName = viewerName;
    }
}
