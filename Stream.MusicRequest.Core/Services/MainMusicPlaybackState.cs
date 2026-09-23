using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Services;

public class MainMusicPlaybackState
{
    public MusicAlbum? Album { get; set; }

    public int TrackIndex { get; set; }

    public TimeSpan Position { get; set; }

    public bool WasPlaying { get; set; }
}