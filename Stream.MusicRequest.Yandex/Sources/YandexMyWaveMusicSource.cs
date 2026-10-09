using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Sources;
using YandexMusic;

namespace Stream.MusicRequest.Yandex.Sources;

/// <summary>
/// Динамический источник Yandex Music «Моя волна».
/// Треки хранятся в одном MusicAlbum, а новые последовательности
/// Rotor загружаются по мере приближения к концу текущей.
/// </summary>
public sealed class YandexMyWaveMusicSource : IPlayableMusicSource, IContinuousMusicSource, IDisposable
{
    private const string StationSeed = "user:onyourwave";
    private const string ApiBaseUrl = "https://api.music.yandex.net";

    private readonly YandexMusicClient _client;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly Dictionary<string, string?> _batchByTrackId = new(StringComparer.Ordinal);
    private string? _radioSessionId;
    private string? _currentBatchId;
    private bool _disposed;

    public YandexMyWaveMusicSource(YandexMusicClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    }

    public string Name => "Yandex Music — Моя волна";

    public bool CanHandleUrl(string url) =>
        string.Equals(url, "mywave://onyourwave", StringComparison.OrdinalIgnoreCase);

    public bool CanHandleTrackUrl(string url) => false;

    public Task<Track?> ResolveTrackAsync(string url) => Task.FromResult<Track?>(null);

    public async Task<MusicAlbum> CreateAlbumAsync()
    {
        if (!_client.Authentication.IsAuthenticated)
            throw new InvalidOperationException("Сначала подключите Yandex Music.");

        var token = _client.Authentication.Session.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Не удалось получить токен Yandex Music.");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("OAuth", token);

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = await CreateSessionAsync().ConfigureAwait(false);
            if (session.Tracks.Count == 0)
                throw new InvalidOperationException("Yandex Music не вернул треки для «Моей волны».");

            _radioSessionId = session.RadioSessionId;
            _currentBatchId = session.BatchId;
            _batchByTrackId.Clear();

            var album = new MusicAlbum
            {
                Id = "mywave",
                Title = "Моя волна",
                SourceUrl = "mywave://onyourwave",
                Source = this
            };

            AddTracks(album, session);
            FileLogger.Info($"Моя волна: rotor session создана, получено треков: {album.Tracks.Count}.");
            return album;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task<int> AppendNextBatchAsync(MusicAlbum album)
    {
        if (album.Source != this)
            throw new ArgumentException("Передан альбом не от источника «Моя волна».", nameof(album));

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(_radioSessionId))
                throw new InvalidOperationException("Rotor session «Моей волны» не создана.");

            var lastTrackId = album.Tracks.LastOrDefault()?.Id;
            var session = await RequestNextSequenceAsync(
                _radioSessionId,
                string.IsNullOrWhiteSpace(lastTrackId)
                    ? Array.Empty<string>()
                    : new[] { lastTrackId }).ConfigureAwait(false);

            if (session.UnknownSession || session.Terminated)
            {
                FileLogger.Info("Rotor session завершена или неизвестна; создаётся новая сессия.");
                session = await CreateSessionAsync().ConfigureAwait(false);
                _radioSessionId = session.RadioSessionId;
            }

            var before = album.Tracks.Count;
            AddTracks(album, session);
            var added = album.Tracks.Count - before;

            // Rotor может повторить уже выданные треки. В таком случае
            // пробуем запросить ещё несколько sequence, чтобы не зациклиться
            // на конце локальной очереди.
            for (var attempt = 0; added == 0 && attempt < 2; attempt++)
            {
                var lastId = album.Tracks.LastOrDefault()?.Id;
                session = await RequestNextSequenceAsync(
                    _radioSessionId!,
                    string.IsNullOrWhiteSpace(lastId) ? Array.Empty<string>() : new[] { lastId })
                    .ConfigureAwait(false);

                if (session.UnknownSession || session.Terminated)
                {
                    session = await CreateSessionAsync().ConfigureAwait(false);
                    _radioSessionId = session.RadioSessionId;
                }

                before = album.Tracks.Count;
                AddTracks(album, session);
                added = album.Tracks.Count - before;
            }

            _currentBatchId = session.BatchId;
            FileLogger.Info($"Моя волна: загружена следующая sequence, добавлено треков: {added}.");
            return added;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task<string?> GetPlaybackUrlAsync(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Id))
            return null;

        try
        {
            if (_batchByTrackId.TryGetValue(track.Id, out var batchId))
                await SendFeedbackAsync(track.Id, "trackStarted", null, batchId).ConfigureAwait(false);

            return await _client.Tracks.GetDirectLinkAsync(track.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Error($"Моя волна: не удалось получить аудиоссылку для трека {track.Id}.", ex);
            return null;
        }
    }

    public Task TrackFinishedAsync(Track track, TimeSpan played) =>
        SendTrackFeedbackForTrackAsync(track, "trackFinished", played);

    public Task TrackSkippedAsync(Track track, TimeSpan played) =>
        SendTrackFeedbackForTrackAsync(track, "skip", played);

    private async Task SendTrackFeedbackForTrackAsync(Track track, string type, TimeSpan played)
    {
        if (string.IsNullOrWhiteSpace(track.Id) ||
            !_batchByTrackId.TryGetValue(track.Id, out var batchId))
            return;

        try
        {
            await SendFeedbackAsync(
                track.Id,
                type,
                Math.Max(0, played.TotalSeconds),
                batchId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Info($"Моя волна: feedback {type} не отправлен: {ex.Message}");
        }
    }

    private async Task<SessionResponse> CreateSessionAsync()
    {
        var json = JsonSerializer.Serialize(new
        {
            seeds = new[] { StationSeed },
            includeTracksInResponse = true
        });

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"{ApiBaseUrl}/rotor/session/new", content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ParseSession(body);
    }

    private async Task<SessionResponse> RequestNextSequenceAsync(string sessionId, IReadOnlyCollection<string> queue)
    {
        var json = JsonSerializer.Serialize(new { queue });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(
            $"{ApiBaseUrl}/rotor/session/{Uri.EscapeDataString(sessionId)}/tracks",
            content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ParseSession(body);
    }

    private async Task SendFeedbackAsync(string trackId, string type, double? playedSeconds, string? batchId)
    {
        if (string.IsNullOrWhiteSpace(_radioSessionId) || string.IsNullOrWhiteSpace(batchId))
            return;

        var eventData = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["trackId"] = trackId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };
        if (playedSeconds.HasValue)
            eventData["totalPlayedSeconds"] = playedSeconds.Value;

        var json = JsonSerializer.Serialize(new { @event = eventData, batchId });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(
            $"{ApiBaseUrl}/rotor/session/{Uri.EscapeDataString(_radioSessionId)}/feedback",
            content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        FileLogger.Info($"Моя волна: feedback {type} отправлен для трека {trackId}.");
    }

    private void AddTracks(MusicAlbum album, SessionResponse session)
    {
        foreach (var item in session.Tracks)
        {
            if (string.IsNullOrWhiteSpace(item.Id) ||
                album.Tracks.Any(existing => existing.Id == item.Id))
                continue;

            var track = new Track
            {
                Id = item.Id,
                Title = item.Title ?? "Неизвестный трек",
                Artist = string.IsNullOrWhiteSpace(item.Artist) ? "Неизвестный исполнитель" : item.Artist,
                Album = item.Album,
                Duration = TimeSpan.FromMilliseconds(Math.Max(0, item.DurationMs)),
                CoverUrl = BuildCoverUrl(item.CoverUri),
                RequestSource = "Моя волна",
                RequestMode = "Радио"
            };
            album.Tracks.Add(track);
            _batchByTrackId[track.Id] = session.BatchId;
        }
    }

    private static SessionResponse ParseSession(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("В ответе Rotor отсутствует result.");

        var sessionId = GetString(result, "radioSessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("Rotor не вернул radioSessionId.");

        var tracks = new List<RotorTrack>();
        if (result.TryGetProperty("sequence", out var sequence) && sequence.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in sequence.EnumerateArray())
            {
                if (!entry.TryGetProperty("track", out var track) || track.ValueKind != JsonValueKind.Object)
                    continue;

                var artists = track.TryGetProperty("artists", out var artistArray) &&
                              artistArray.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", artistArray.EnumerateArray()
                        .Select(artist => GetString(artist, "name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name)))
                    : string.Empty;

                var albums = track.TryGetProperty("albums", out var albumArray) &&
                             albumArray.ValueKind == JsonValueKind.Array
                    ? albumArray.EnumerateArray().Select(album => GetString(album, "title"))
                        .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title))
                    : null;

                var durationMs = track.TryGetProperty("durationMs", out var duration) &&
                                 duration.TryGetInt64(out var value) ? value : 0;

                tracks.Add(new RotorTrack(
                    GetString(track, "id") ?? string.Empty,
                    GetString(track, "title"),
                    artists,
                    albums,
                    durationMs,
                    GetString(track, "coverUri")));
            }
        }

        return new SessionResponse(
            sessionId,
            GetString(result, "batchId"),
            tracks,
            result.TryGetProperty("terminated", out var terminated) && terminated.ValueKind == JsonValueKind.True,
            result.TryGetProperty("unknownSession", out var unknown) && unknown.ValueKind == JsonValueKind.True);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    private static string? BuildCoverUrl(string? coverUri)
    {
        if (string.IsNullOrWhiteSpace(coverUri))
            return null;
        var value = coverUri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? coverUri : $"https://{coverUri}";
        return value.Contains("%%", StringComparison.Ordinal) ? value.Replace("%%", "400x400", StringComparison.Ordinal) : value;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _httpClient.Dispose();
        _sessionGate.Dispose();
    }

    private sealed record RotorTrack(string Id, string? Title, string Artist, string? Album, long DurationMs, string? CoverUri);
    private sealed record SessionResponse(string RadioSessionId, string? BatchId, List<RotorTrack> Tracks, bool Terminated, bool UnknownSession);
}
