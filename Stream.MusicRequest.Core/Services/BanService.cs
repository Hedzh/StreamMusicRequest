using Stream.MusicRequest.Core.Infrastructure.Storage;
using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Services;

public class BanService : IBanService
{
    private readonly object _syncRoot = new();

    private readonly IBanStorage _storage;

    private readonly List<BannedTrack> _bannedTracks;

    private readonly List<BannedUser> _bannedUsers;

    public BanService(
        IBanStorage storage)
    {
        _storage =
            storage
            ?? throw new ArgumentNullException(
                nameof(storage));

        var data =
            _storage.Load();

        _bannedTracks =
            data.Tracks ?? new List<BannedTrack>();

        _bannedUsers =
            data.Users ?? new List<BannedUser>();
    }

    public bool IsUserBanned(
        string username)
    {
        var normalizedUsername =
            NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(
                normalizedUsername))
        {
            return false;
        }

        lock (_syncRoot)
        {
            return _bannedUsers.Any(
                user =>
                    string.Equals(
                        user.Username,
                        normalizedUsername,
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsTrackBanned(
        string source,
        string trackId)
    {
        var normalizedSource =
            NormalizeKey(source);

        var normalizedTrackId =
            NormalizeKey(trackId);

        if (string.IsNullOrWhiteSpace(normalizedSource) ||
            string.IsNullOrWhiteSpace(normalizedTrackId))
        {
            return false;
        }

        lock (_syncRoot)
        {
            return _bannedTracks.Any(
                track =>
                    string.Equals(
                        track.Source,
                        normalizedSource,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        track.TrackId,
                        normalizedTrackId,
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    public void BanUser(
        string username)
    {
        var normalizedUsername =
            NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(
                normalizedUsername))
        {
            throw new ArgumentException(
                "Имя пользователя не указано.",
                nameof(username));
        }

        lock (_syncRoot)
        {
            if (_bannedUsers.Any(
                    user =>
                        string.Equals(
                            user.Username,
                            normalizedUsername,
                            StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _bannedUsers.Add(
                new BannedUser
                {
                    Username = normalizedUsername,
                    BannedAt = DateTime.UtcNow
                });

            Save();
        }
    }

    public void UnbanUser(
        string username)
    {
        var normalizedUsername =
            NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(
                normalizedUsername))
        {
            return;
        }

        lock (_syncRoot)
        {
            _bannedUsers.RemoveAll(
                user =>
                    string.Equals(
                        user.Username,
                        normalizedUsername,
                        StringComparison.OrdinalIgnoreCase));

            Save();
        }
    }

    public void BanTrack(
        string source,
        string trackId,
        string title,
        string artist,
        string sourceUrl)
    {
        var normalizedSource =
            NormalizeKey(source);

        var normalizedTrackId =
            NormalizeKey(trackId);

        if (string.IsNullOrWhiteSpace(normalizedSource))
        {
            throw new ArgumentException(
                "Источник трека не указан.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(normalizedTrackId))
        {
            throw new ArgumentException(
                "Идентификатор трека не указан.",
                nameof(trackId));
        }

        lock (_syncRoot)
        {
            var existing =
                _bannedTracks.FirstOrDefault(
                    track =>
                        string.Equals(
                            track.Source,
                            normalizedSource,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            track.TrackId,
                            normalizedTrackId,
                            StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                return;
            }

            _bannedTracks.Add(
                new BannedTrack
                {
                    Source = normalizedSource,
                    TrackId = normalizedTrackId,
                    Title = title?.Trim() ?? string.Empty,
                    Artist = artist?.Trim() ?? string.Empty,
                    SourceUrl = sourceUrl?.Trim() ?? string.Empty,
                    BannedAt = DateTime.UtcNow
                });

            Save();
        }
    }

    public void UnbanTrack(
        string source,
        string trackId)
    {
        var normalizedSource =
            NormalizeKey(source);

        var normalizedTrackId =
            NormalizeKey(trackId);

        if (string.IsNullOrWhiteSpace(normalizedSource) ||
            string.IsNullOrWhiteSpace(normalizedTrackId))
        {
            return;
        }

        lock (_syncRoot)
        {
            _bannedTracks.RemoveAll(
                track =>
                    string.Equals(
                        track.Source,
                        normalizedSource,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        track.TrackId,
                        normalizedTrackId,
                        StringComparison.OrdinalIgnoreCase));

            Save();
        }
    }

    public IReadOnlyList<BannedUser> GetBannedUsers()
    {
        lock (_syncRoot)
        {
            return _bannedUsers
                .Select(
                    user =>
                        new BannedUser
                        {
                            Username = user.Username,
                            BannedAt = user.BannedAt
                        })
                .ToList();
        }
    }

    public IReadOnlyList<BannedTrack> GetBannedTracks()
    {
        lock (_syncRoot)
        {
            return _bannedTracks
                .Select(
                    track =>
                        new BannedTrack
                        {
                            Source = track.Source,
                            TrackId = track.TrackId,
                            Title = track.Title,
                            Artist = track.Artist,
                            SourceUrl = track.SourceUrl,
                            BannedAt = track.BannedAt
                        })
                .ToList();
        }
    }

    private void Save()
    {
        _storage
            .SaveAsync(
                new BanStorageModel
                {
                    Tracks = _bannedTracks
                        .Select(
                            track =>
                                new BannedTrack
                                {
                                    Source = track.Source,
                                    TrackId = track.TrackId,
                                    Title = track.Title,
                                    Artist = track.Artist,
                                    SourceUrl = track.SourceUrl,
                                    BannedAt = track.BannedAt
                                })
                        .ToList(),

                    Users = _bannedUsers
                        .Select(
                            user =>
                                new BannedUser
                                {
                                    Username = user.Username,
                                    BannedAt = user.BannedAt
                                })
                        .ToList()
                })
            .GetAwaiter()
            .GetResult();
    }

    private static string NormalizeUsername(
        string username)
    {
        return username?.Trim() ?? string.Empty;
    }

    private static string NormalizeKey(
        string value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
