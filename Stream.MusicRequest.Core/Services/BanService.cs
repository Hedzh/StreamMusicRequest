using Stream.MusicRequest.Core.Infrastructure.Storage;
using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Services;

public class BanService : IBanService
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    private readonly IBanStorage _storage;
    private readonly List<BannedTrack> _bannedTracks;
    private readonly List<BannedUser> _bannedUsers;

    public event EventHandler? BanChanged;

    public BanService(IBanStorage storage)
    {
        _storage =
            storage
            ?? throw new ArgumentNullException(nameof(storage));

        var data = _storage.Load();

        _bannedTracks =
            data.Tracks ?? new List<BannedTrack>();

        _bannedUsers =
            data.Users ?? new List<BannedUser>();
    }

    public bool IsUserBanned(string username)
    {
        var normalizedUsername = NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return false;

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

    public bool IsTrackBanned(string source, string trackId)
    {
        var normalizedSource = NormalizeKey(source);
        var normalizedTrackId = NormalizeKey(trackId);

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

    public async Task BanUserAsync(string username)
    {
        var normalizedUsername = NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new ArgumentException(
                "Имя пользователя не указано.",
                nameof(username));
        }

        await _mutationGate.WaitAsync().ConfigureAwait(false);

        try
        {
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
            }

            try
            {
                await SaveAsync().ConfigureAwait(false);
            }
            catch
            {
                lock (_syncRoot)
                {
                    _bannedUsers.RemoveAll(
                        user =>
                            string.Equals(
                                user.Username,
                                normalizedUsername,
                                StringComparison.OrdinalIgnoreCase));
                }

                throw;
            }

            BanChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task UnbanUserAsync(string username)
    {
        var normalizedUsername = NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return;

        await _mutationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            List<BannedUser> removedUsers;

            lock (_syncRoot)
            {
                removedUsers =
                    _bannedUsers
                        .Where(
                            user =>
                                string.Equals(
                                    user.Username,
                                    normalizedUsername,
                                    StringComparison.OrdinalIgnoreCase))
                        .Select(
                            user =>
                                new BannedUser
                                {
                                    Username = user.Username,
                                    BannedAt = user.BannedAt
                                })
                        .ToList();

                if (removedUsers.Count == 0)
                    return;

                _bannedUsers.RemoveAll(
                    user =>
                        string.Equals(
                            user.Username,
                            normalizedUsername,
                            StringComparison.OrdinalIgnoreCase));
            }

            try
            {
                await SaveAsync().ConfigureAwait(false);
            }
            catch
            {
                lock (_syncRoot)
                {
                    _bannedUsers.AddRange(removedUsers);
                }

                throw;
            }

            BanChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task BanTrackAsync(
        string source,
        string trackId,
        string title,
        string artist,
        string sourceUrl)
    {
        var normalizedSource = NormalizeKey(source);
        var normalizedTrackId = NormalizeKey(trackId);

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

        await _mutationGate.WaitAsync().ConfigureAwait(false);

        try
        {
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
                    return;

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
            }

            try
            {
                await SaveAsync().ConfigureAwait(false);
            }
            catch
            {
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
                }

                throw;
            }

            BanChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task UnbanTrackAsync(
        string source,
        string trackId)
    {
        var normalizedSource = NormalizeKey(source);
        var normalizedTrackId = NormalizeKey(trackId);

        if (string.IsNullOrWhiteSpace(normalizedSource) ||
            string.IsNullOrWhiteSpace(normalizedTrackId))
        {
            return;
        }

        await _mutationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            List<BannedTrack> removedTracks;

            lock (_syncRoot)
            {
                removedTracks =
                    _bannedTracks
                        .Where(
                            track =>
                                string.Equals(
                                    track.Source,
                                    normalizedSource,
                                    StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(
                                    track.TrackId,
                                    normalizedTrackId,
                                    StringComparison.OrdinalIgnoreCase))
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

                if (removedTracks.Count == 0)
                    return;

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
            }

            try
            {
                await SaveAsync().ConfigureAwait(false);
            }
            catch
            {
                lock (_syncRoot)
                {
                    _bannedTracks.AddRange(removedTracks);
                }

                throw;
            }

            BanChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _mutationGate.Release();
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

    private async Task SaveAsync()
    {
        BanStorageModel data;

        lock (_syncRoot)
        {
            data =
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
                };
        }

        await _storage.SaveAsync(data).ConfigureAwait(false);
    }

    private static string NormalizeUsername(string username)
    {
        return username?.Trim() ?? string.Empty;
    }

    private static string NormalizeKey(string value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
