using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Player;

public interface IMusicPlayer
{
    event EventHandler? PlaybackFinished;

    Task PlayAsync(Track track);
    Task PlayAsync(Track track, TimeSpan startPosition);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task SkipAsync();

    Task SeekAsync(TimeSpan position);

    void SetVolume(float volume);

    bool IsPlaying { get; }

    Track? GetCurrentTrack();

    TimeSpan GetPosition();

    TimeSpan GetDuration();
}