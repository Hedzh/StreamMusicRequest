using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Services;

public interface IBanService
{
    bool IsUserBanned(string username);

    bool IsTrackBanned(string source, string trackId);

    Task BanUserAsync(string username);

    Task UnbanUserAsync(string username);

    Task BanTrackAsync(
        string source,
        string trackId,
        string title,
        string artist,
        string sourceUrl);

    Task UnbanTrackAsync(string source, string trackId);

    IReadOnlyList<BannedUser> GetBannedUsers();

    IReadOnlyList<BannedTrack> GetBannedTracks();

    event EventHandler? BanChanged;
}