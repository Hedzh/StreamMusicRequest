namespace Stream.MusicRequest.Core.Models;

public class Track
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string? Album { get; set; }

    public TimeSpan Duration { get; set; }

    public string? CoverUrl { get; set; }

    public string? SourceUrl { get; set; }

    public string? RequestedBy { get; set; }
}
