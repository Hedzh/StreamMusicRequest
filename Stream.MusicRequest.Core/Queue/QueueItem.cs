using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Core.Queue;

public class QueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public Requests.MusicRequest Request { get; set; } = new();

    public Track? Track { get; set; }

    public IMusicSource? Source { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public TimeSpan ResumePosition { get; set; }

    public bool ResumePlayback { get; set; }
}
