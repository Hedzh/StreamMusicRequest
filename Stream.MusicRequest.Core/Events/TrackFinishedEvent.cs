using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Events;

public class TrackFinishedEvent
{
    public Track Track { get; }

    public string ViewerName { get; }

    public TrackFinishedEvent(Track track, string viewerName)
    {
        Track = track;
        ViewerName = viewerName;
    }
}
