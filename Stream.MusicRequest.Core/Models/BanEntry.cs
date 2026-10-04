namespace Stream.MusicRequest.Core.Models;

public class BannedTrack
{
    public string Source { get; set; } = string.Empty;

    public string TrackId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public DateTime BannedAt { get; set; } = DateTime.UtcNow;
}

public class BannedUser
{
    public string Username { get; set; } = string.Empty;

    public DateTime BannedAt { get; set; } = DateTime.UtcNow;
}

public class BanStorageModel
{
    public List<BannedTrack> Tracks { get; set; } = new();

    public List<BannedUser> Users { get; set; } = new();
}
