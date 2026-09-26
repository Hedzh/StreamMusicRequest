using YandexMusic;
using YandexTrack = YandexMusic.Models.Tracks.Track;
using YandexTrackShort = YandexMusic.Models.Tracks.TrackShort;
using CoreTrack = Stream.MusicRequest.Core.Models.Track;
using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Yandex.Sources;

public class YandexMusicSource :
    IPlayableMusicSource,
    IAlbumMusicSource,
    IPlaylistMusicSource
{
    private readonly YandexMusicClient _client;

    public YandexMusicSource(
        YandexMusicClient client)
    {
        _client = client;
    }

    public string Name =>
        "Yandex Music";

    public bool CanHandleUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        if (!uri.Host.Equals(
                "music.yandex.ru",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        /*
         * Если URL содержит /track/,
         * он считается ссылкой именно на трек.
         *
         * Это важно для ссылок вида:
         *
         * /album/41545061/track/135395171
         *
         * Несмотря на наличие /album/,
         * пользователь заказывает конкретный трек.
         */
        return ContainsPathSegment(
            uri,
            "track");
    }

    public bool CanHandleAlbumUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        if (!uri.Host.Equals(
                "music.yandex.ru",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        /*
         * URL с /track/ всегда имеет приоритет
         * и не должен считаться альбомом.
         */
        if (ContainsPathSegment(
                uri,
                "track"))
        {
            return false;
        }

        return ContainsPathSegment(
            uri,
            "album");
    }

    public bool CanHandlePlaylistUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        if (!uri.Host.Equals(
                "music.yandex.ru",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        /*
         * Плейлист не должен пересекаться
         * с URL конкретного трека.
         */
        if (ContainsPathSegment(
                uri,
                "track"))
        {
            return false;
        }

        return ContainsPathSegment(
            uri,
            "playlists");
    }

    public async Task<CoreTrack?> ResolveTrackAsync(
        string url)
    {
        if (!CanHandleUrl(url))
        {
            return null;
        }

        var trackId =
            ExtractTrackId(url);

        if (trackId == null)
        {
            return null;
        }

        var yandexTrack =
            await _client.Tracks.GetAsync(
                trackId);

        if (yandexTrack == null)
        {
            return null;
        }

        return ConvertTrack(
            yandexTrack);
    }

    public async Task<MusicAlbum?> ResolveAlbumAsync(
        string url)
    {
        if (!CanHandleAlbumUrl(url))
        {
            return null;
        }

        var albumId =
            ExtractAlbumId(url);

        if (albumId == null)
        {
            return null;
        }

        var yandexAlbum =
            await _client.Albums.GetWithTracksAsync(
                albumId);

        if (yandexAlbum == null)
        {
            return null;
        }

        var album =
            new MusicAlbum
            {
                Id =
                    yandexAlbum.Id,

                Title =
                    yandexAlbum.Title,

                SourceUrl =
                    url,

                Source =
                    this
            };

        if (yandexAlbum.Artists.Count > 0)
        {
            album.Artist =
                yandexAlbum.Artists[0].Name;
        }

        if (yandexAlbum.Volumes != null)
        {
            foreach (var volume in
                     yandexAlbum.Volumes)
            {
                foreach (var yandexTrack in
                         volume)
                {
                    var track =
                        ConvertTrack(
                            yandexTrack);

                    album.Tracks.Add(
                        track);
                }
            }
        }

        return album;
    }

    public async Task<MusicAlbum?> ResolvePlaylistAsync(
        string url)
    {
        if (!CanHandlePlaylistUrl(url))
        {
            return null;
        }

        var playlistUuid =
            ExtractPlaylistUuid(url);

        if (playlistUuid == null)
        {
            return null;
        }

        var yandexPlaylist =
            await _client.Playlists.GetByUuidAsync(
                playlistUuid);

        if (yandexPlaylist == null)
        {
            return null;
        }

        var playlist =
            new MusicAlbum
            {
                Id =
                    yandexPlaylist.PlaylistUuid
                    ?? playlistUuid,

                Title =
                    yandexPlaylist.Title,

                SourceUrl =
                    url,

                Source =
                    this
            };

        foreach (var trackShort in
                 yandexPlaylist.Tracks)
        {
            var yandexTrack =
                await ResolvePlaylistTrackAsync(
                    trackShort);

            if (yandexTrack == null)
            {
                continue;
            }

            var track =
                ConvertTrack(
                    yandexTrack);

            playlist.Tracks.Add(
                track);
        }

        return playlist;
    }

    public async Task<string?> GetPlaybackUrlAsync(
        CoreTrack track)
    {
        if (string.IsNullOrWhiteSpace(
                track.Id))
        {
            return null;
        }

        const int maxAttempts = 3;

        for (var attempt = 1;
             attempt <= maxAttempts;
             attempt++)
        {
            try
            {
                return await _client.Tracks.GetDirectLinkAsync(
                    track.Id);
            }
            catch (Exception ex)
            {
                FileLogger.Info(
                    $"Не удалось получить прямую ссылку " +
                    $"Yandex Music для трека {track.Id}. " +
                    $"Попытка {attempt}/{maxAttempts}: " +
                    $"{ex.Message}");

                if (attempt >= maxAttempts)
                {
                    FileLogger.Info(
                        $"Трек {track.Id} будет пропущен " +
                        "из-за недоступности прямой ссылки.");

                    return null;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(750));
            }
        }

        return null;
    }

    private async Task<YandexTrack?> ResolvePlaylistTrackAsync(
        YandexTrackShort trackShort)
    {
        if (trackShort.Track != null)
        {
            return trackShort.Track;
        }

        if (string.IsNullOrWhiteSpace(
                trackShort.Id))
        {
            return null;
        }

        return await _client.Tracks.GetAsync(
            trackShort.Id);
    }

    private static CoreTrack ConvertTrack(
        YandexTrack yandexTrack)
    {
        var artist =
            yandexTrack.Artists.Count > 0
                ? yandexTrack.Artists[0].Name
                : "Неизвестный исполнитель";

        var album =
            yandexTrack.Albums.Count > 0
                ? yandexTrack.Albums[0].Title
                : null;

        return new CoreTrack
        {
            Id =
                yandexTrack.Id,

            Title =
                yandexTrack.Title,

            Artist =
                artist,

            Album =
                album,

            Duration =
                TimeSpan.FromMilliseconds(
                    yandexTrack.DurationMs),
            SourceUrl =
                null
        };
    }

    private static string? ExtractTrackId(
        string url)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return null;
        }

        var segments =
            uri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0;
             i < segments.Length - 1;
             i++)
        {
            if (segments[i].Equals(
                    "track",
                    StringComparison.OrdinalIgnoreCase))
            {
                var trackId =
                    segments[i + 1];

                if (!string.IsNullOrWhiteSpace(
                        trackId))
                {
                    return trackId;
                }
            }
        }

        return null;
    }

    private static string? ExtractAlbumId(
        string url)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return null;
        }

        var segments =
            uri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0;
             i < segments.Length - 1;
             i++)
        {
            if (segments[i].Equals(
                    "album",
                    StringComparison.OrdinalIgnoreCase))
            {
                var albumId =
                    segments[i + 1];

                if (!string.IsNullOrWhiteSpace(
                        albumId))
                {
                    return albumId;
                }
            }
        }

        return null;
    }

    private static string? ExtractPlaylistUuid(
        string url)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return null;
        }

        var segments =
            uri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0;
             i < segments.Length - 1;
             i++)
        {
            if (segments[i].Equals(
                    "playlists",
                    StringComparison.OrdinalIgnoreCase))
            {
                var playlistUuid =
                    segments[i + 1];

                if (!string.IsNullOrWhiteSpace(
                        playlistUuid))
                {
                    return playlistUuid;
                }
            }
        }

        return null;
    }

    private static bool ContainsPathSegment(
        Uri uri,
        string segment)
    {
        var segments =
            uri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(
            value =>
                value.Equals(
                    segment,
                    StringComparison.OrdinalIgnoreCase));
    }
}