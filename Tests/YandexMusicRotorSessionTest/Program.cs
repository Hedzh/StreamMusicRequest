using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YandexMusic;

const string station = "user:onyourwave";
const string apiBaseUrl = "https://api.music.yandex.net";

Console.WriteLine("====================================================");
Console.WriteLine(" Stream Music Request — тест feedback rotor/session");
Console.WriteLine(" Yandex Music «Моя волна»");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("Отдельный диагностический тест.");
Console.WriteLine("Основная архитектура SMR и тест «Моя волна» не изменяются.");
Console.WriteLine();
Console.WriteLine("Тестирует:");
Console.WriteLine("  • trackStarted");
Console.WriteLine("  • trackFinished");
Console.WriteLine("  • skip");
Console.WriteLine("После feedback запрашивается следующая sequence через queue: [].");
Console.WriteLine();

await using var client = new YandexMusicClient();

var token = Environment.GetEnvironmentVariable("YANDEX_MUSIC_TOKEN");

if (!string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Используется YANDEX_MUSIC_TOKEN.");
    client.Authentication.SignInWithToken(token);
}
else
{
    Console.WriteLine("YANDEX_MUSIC_TOKEN не задан.");
    Console.WriteLine("Запускаю авторизацию Yandex Music через код устройства.");
    Console.WriteLine();

    await client.Authentication.SignInWithDeviceFlowAsync(
        code =>
        {
            Console.WriteLine("Откройте:");
            Console.WriteLine(code.VerificationUrl);
            Console.WriteLine();
            Console.WriteLine($"Код: {code.UserCode}");
            Console.WriteLine();
            Console.WriteLine("После подтверждения авторизации здесь продолжится тест.");
        });
}

if (!client.Authentication.IsAuthenticated)
{
    Console.WriteLine("ОШИБКА: авторизация Yandex Music не выполнена.");
    return 1;
}

var accessToken = token ?? client.Authentication.Session.AccessToken;

if (string.IsNullOrWhiteSpace(accessToken))
{
    Console.WriteLine("ОШИБКА: OAuth-токен не найден.");
    return 2;
}

Console.WriteLine();
Console.WriteLine("Авторизация успешна.");
Console.WriteLine();

var status = await client.Account.GetStatusAsync();
Console.WriteLine(
    $"Аккаунт: {status?.Account?.DisplayName ?? status?.Account?.Login ?? "не удалось определить"}");

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("OAuth", accessToken);

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" СОЗДАНИЕ ROTOR SESSION");
Console.WriteLine("==========================================");
Console.WriteLine();

var session = await CreateSessionAsync(http);

if (session == null)
{
    return 3;
}

PrintSessionSummary(session, "Первая sequence");

if (session.Tracks.Count < 3 ||
    session.Tracks.Take(3).Any(track => string.IsNullOrWhiteSpace(track.Id)))
{
    Console.WriteLine("ОШИБКА: для feedback нужно минимум 3 трека с trackId.");
    return 4;
}

var firstTrack = session.Tracks[0];
var secondTrack = session.Tracks[1];
var thirdTrack = session.Tracks[2];

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" FEEDBACK");
Console.WriteLine("==========================================");
Console.WriteLine();

Console.WriteLine("1) trackStarted — первый трек");
if (!await SendFeedbackAsync(
        http,
        session.RadioSessionId,
        session.BatchId,
        firstTrack.Id!,
        "trackStarted",
        totalPlayedSeconds: null))
{
    return 5;
}

Console.WriteLine();
Console.WriteLine("2) trackFinished — первый трек, условно проиграно 30 секунд");
if (!await SendFeedbackAsync(
        http,
        session.RadioSessionId,
        session.BatchId,
        firstTrack.Id!,
        "trackFinished",
        totalPlayedSeconds: 30))
{
    return 6;
}

Console.WriteLine();
Console.WriteLine("3) trackStarted — второй трек");
if (!await SendFeedbackAsync(
        http,
        session.RadioSessionId,
        session.BatchId,
        secondTrack.Id!,
        "trackStarted",
        totalPlayedSeconds: null))
{
    return 7;
}

Console.WriteLine();
Console.WriteLine("4) skip — второй трек, условно проиграно 5 секунд");
if (!await SendFeedbackAsync(
        http,
        session.RadioSessionId,
        session.BatchId,
        secondTrack.Id!,
        "skip",
        totalPlayedSeconds: 5))
{
    return 8;
}

Console.WriteLine();
Console.WriteLine("5) trackStarted — третий трек");
if (!await SendFeedbackAsync(
        http,
        session.RadioSessionId,
        session.BatchId,
        thirdTrack.Id!,
        "trackStarted",
        totalPlayedSeconds: null))
{
    return 9;
}

Console.WriteLine();
Console.WriteLine("6) trackFinished — третий трек, условно проиграно 30 секунд");
if (!await SendFeedbackAsync(
        http,
        session.RadioSessionId,
        session.BatchId,
        thirdTrack.Id!,
        "trackFinished",
        totalPlayedSeconds: 30))
{
    return 10;
}

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ПРОДОЛЖЕНИЕ ПОСЛЕ FEEDBACK");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("Запрашиваем следующую sequence с queue: [].");
Console.WriteLine("Смотрим, изменился ли состав рекомендаций после feedback.");
Console.WriteLine();

var nextSession = await RequestNextSequenceAsync(
    http,
    session.RadioSessionId,
    Array.Empty<string>());

if (nextSession == null)
{
    return 11;
}

PrintSessionSummary(nextSession, "Sequence после feedback");

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ИТОГ");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("В одной rotor session отправлены:");
Console.WriteLine("  • trackStarted для первого трека;");
Console.WriteLine("  • trackFinished для первого трека;");
Console.WriteLine("  • trackStarted для второго трека;");
Console.WriteLine("  • skip для второго трека;");
Console.WriteLine("  • trackStarted для третьего трека;");
Console.WriteLine("  • trackFinished для третьего трека.");
Console.WriteLine();
Console.WriteLine("После этого запрошена следующая sequence.");
Console.WriteLine("Нужно сравнить её с первой sequence и посмотреть,");
Console.WriteLine("как feedback повлиял на дальнейшие рекомендации.");
Console.WriteLine();
Console.WriteLine("Тест завершён.");

return 0;

static async Task<SessionResult?> CreateSessionAsync(HttpClient http)
{
    var requestBody = new
    {
        seeds = new[] { station },
        includeTracksInResponse = true
    };

    var requestJson = JsonSerializer.Serialize(requestBody);

    Console.WriteLine("POST /rotor/session/new");
    Console.WriteLine($"Body: {requestJson}");
    Console.WriteLine();

    using var content = new StringContent(
        requestJson,
        Encoding.UTF8,
        "application/json");

    using var response = await http.PostAsync(
        $"{apiBaseUrl}/rotor/session/new",
        content);

    var responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine("Ответ:");
    Console.WriteLine(responseText);
    Console.WriteLine();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine("ОШИБКА: создание rotor session не принято.");
        return null;
    }

    using var json = JsonDocument.Parse(responseText);
    var root = json.RootElement;

    if (!root.TryGetProperty("result", out var result) ||
        result.ValueKind != JsonValueKind.Object)
    {
        Console.WriteLine("ОШИБКА: в ответе отсутствует объект result.");
        return null;
    }

    var radioSessionId = result.TryGetProperty("radioSessionId", out var sessionElement)
        ? sessionElement.GetString()
        : null;

    if (string.IsNullOrWhiteSpace(radioSessionId))
    {
        Console.WriteLine("ОШИБКА: в result отсутствует radioSessionId.");
        return null;
    }

    if (!result.TryGetProperty("sequence", out var sequence) ||
        sequence.ValueKind != JsonValueKind.Array)
    {
        Console.WriteLine("ОШИБКА: в result отсутствует sequence.");
        return null;
    }

    var tracks = ParseTracks(sequence);

    var batchId = result.TryGetProperty("batchId", out var batchElement)
        ? batchElement.GetString()
        : null;

    var terminated = result.TryGetProperty("terminated", out var terminatedElement) &&
                     terminatedElement.ValueKind == JsonValueKind.True;

    var unknownSession = result.TryGetProperty("unknownSession", out var unknownElement) &&
                         unknownElement.ValueKind == JsonValueKind.True;

    return new SessionResult(
        radioSessionId,
        batchId,
        tracks,
        terminated,
        unknownSession);
}

static async Task<bool> SendFeedbackAsync(
    HttpClient http,
    string radioSessionId,
    string? batchId,
    string trackId,
    string eventType,
    double? totalPlayedSeconds)
{
    var eventData = new Dictionary<string, object?>
    {
        ["type"] = eventType,
        ["trackId"] = trackId,
        ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
    };

    if (totalPlayedSeconds.HasValue)
    {
        eventData["totalPlayedSeconds"] = totalPlayedSeconds.Value;
    }

    var requestBody = new
    {
        @event = eventData,
        batchId
    };

    var requestJson = JsonSerializer.Serialize(requestBody);

    Console.WriteLine($"POST /rotor/session/{radioSessionId}/feedback");
    Console.WriteLine($"Event: {eventType}");
    Console.WriteLine($"Body: {requestJson}");

    using var content = new StringContent(
        requestJson,
        Encoding.UTF8,
        "application/json");

    using var response = await http.PostAsync(
        $"{apiBaseUrl}/rotor/session/{Uri.EscapeDataString(radioSessionId)}/feedback",
        content);

    var responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine($"Ответ: {responseText}");
    Console.WriteLine();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"ОШИБКА: feedback {eventType} не принят.");
        return false;
    }

    return true;
}

static async Task<SessionResult?> RequestNextSequenceAsync(
    HttpClient http,
    string radioSessionId,
    IReadOnlyCollection<string> trackIds)
{
    var requestBody = new
    {
        queue = trackIds
    };

    var requestJson = JsonSerializer.Serialize(requestBody);

    Console.WriteLine($"POST /rotor/session/{radioSessionId}/tracks");
    Console.WriteLine($"Body: {requestJson}");
    Console.WriteLine();

    using var content = new StringContent(
        requestJson,
        Encoding.UTF8,
        "application/json");

    using var response = await http.PostAsync(
        $"{apiBaseUrl}/rotor/session/{Uri.EscapeDataString(radioSessionId)}/tracks",
        content);

    var responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine("Ответ:");
    Console.WriteLine(responseText);
    Console.WriteLine();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine("ОШИБКА: запрос следующей sequence не принят.");
        return null;
    }

    using var json = JsonDocument.Parse(responseText);
    var root = json.RootElement;

    if (!root.TryGetProperty("result", out var result) ||
        result.ValueKind != JsonValueKind.Object)
    {
        Console.WriteLine("ОШИБКА: в ответе отсутствует объект result.");
        return null;
    }

    if (!result.TryGetProperty("sequence", out var sequence) ||
        sequence.ValueKind != JsonValueKind.Array)
    {
        Console.WriteLine("ОШИБКА: в ответе отсутствует sequence.");
        return null;
    }

    var tracks = ParseTracks(sequence);

    var batchId = result.TryGetProperty("batchId", out var batchElement)
        ? batchElement.GetString()
        : null;

    var terminated = result.TryGetProperty("terminated", out var terminatedElement) &&
                     terminatedElement.ValueKind == JsonValueKind.True;

    var unknownSession = result.TryGetProperty("unknownSession", out var unknownElement) &&
                         unknownElement.ValueKind == JsonValueKind.True;

    return new SessionResult(
        radioSessionId,
        batchId,
        tracks,
        terminated,
        unknownSession);
}

static List<TrackInfo> ParseTracks(JsonElement sequence)
{
    return sequence
        .EnumerateArray()
        .Select(item =>
        {
            if (!item.TryGetProperty("track", out var track) ||
                track.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = track.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;

            var title = track.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()
                : null;

            var artists = track.TryGetProperty("artists", out var artistsElement) &&
                          artistsElement.ValueKind == JsonValueKind.Array
                ? string.Join(
                    ", ",
                    artistsElement
                        .EnumerateArray()
                        .Select(artist =>
                            artist.TryGetProperty("name", out var name)
                                ? name.GetString()
                                : null)
                        .Where(name => !string.IsNullOrWhiteSpace(name)))
                : "Неизвестный исполнитель";

            return new TrackInfo(id, title, artists);
        })
        .Where(track => track != null)
        .Select(track => track!)
        .ToList();
}

static void PrintSessionSummary(SessionResult result, string title)
{
    Console.WriteLine();
    Console.WriteLine($"--- {title} ---");
    Console.WriteLine($"Radio Session ID: {result.RadioSessionId}");
    Console.WriteLine($"Batch ID:         {result.BatchId ?? "не найден"}");
    Console.WriteLine($"Получено треков:  {result.Tracks.Count}");
    Console.WriteLine($"terminated:       {result.Terminated}");
    Console.WriteLine($"unknownSession:   {result.UnknownSession}");
    Console.WriteLine();

    PrintTracks(result.Tracks);
}

static void PrintTracks(IEnumerable<TrackInfo> tracks)
{
    var index = 1;

    foreach (var track in tracks)
    {
        Console.WriteLine(
            $"{index++}. {track.Artists} — {track.Title} [{track.Id}]");
    }
}

record TrackInfo(string? Id, string? Title, string Artists);

record SessionResult(
    string RadioSessionId,
    string? BatchId,
    List<TrackInfo> Tracks,
    bool Terminated,
    bool UnknownSession);
