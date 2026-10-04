using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Services;

public interface IBanService
{
    bool IsUserBanned(string username);

    bool IsTrackBanned(string source, string trackId);

    void BanUser(string username);

    void UnbanUser(string username);

    void BanTrack(
        string source,
        string trackId,
        string title,
        string artist,
        string sourceUrl);

    void UnbanTrack(string source, string trackId);

    IReadOnlyList<BannedUser> GetBannedUsers();

    IReadOnlyList<BannedTrack> GetBannedTracks();
}
