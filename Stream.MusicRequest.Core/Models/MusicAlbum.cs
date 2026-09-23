using System.Collections.ObjectModel;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Core.Models;

public class MusicAlbum
{
    public string Id { get; set; } =
        string.Empty;

    public string Title { get; set; } =
        string.Empty;

    public string? Artist { get; set; }

    public string? CoverUrl { get; set; }

    public string? SourceUrl { get; set; }

    public IMusicSource? Source { get; set; }

    public ObservableCollection<Track> Tracks { get; set; } =
        new();
}