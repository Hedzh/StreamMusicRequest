using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YandexMusic;

const string station = "user:onyourwave";
const string apiBaseUrl = "https://api.music.yandex.net";

Console.WriteLine("====================================================");
Console.WriteLine(" Stream Music Request — тест семантики queue");
Console.WriteLine(" Yandex Music rotor/session");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("Отдельный диагностический тест.");
Console.WriteLine("Основная архитектура SMR и тест «Моя волна» не изменяются.");
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
Console.WriteLine(" ТЕСТ A: ПЕРВАЯ SEQUENCE + ПУСТОЙ QUEUE");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("Создаём новую session и получаем первую sequence.");
Console.WriteLine("Затем отправляем queue: [] в ТОЙ ЖЕ session.");
Console.WriteLine();

var firstSession = await CreateSessionAsync(http, "A");
if (firstSession == null)
{
    return 3;
}

PrintSessionSummary(firstSession, "Первая sequence");

Console.WriteLine();
Console.WriteLine("Отправляем продолжение с ПУСТЫМ queue.");
Console.WriteLine();

var emptyQueueResult = await RequestNextSequenceAsync(
    http,
    firstSession.RadioSessionId,
    Array.Empty<string>(),
    "A / пустой queue");

if (emptyQueueResult == null)
{
    return 4;
}

PrintSessionSummary(emptyQueueResult, "Sequence после пустого queue");

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ТЕСТ B: НОВАЯ SESSION + ВСЕ 5 TRACK ID");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("Создаём ДРУГУЮ session.");
Console.WriteLine("Получаем её первую sequence.");
Console.WriteLine("Затем передаём queue со всеми trackId этой sequence.");
Console.WriteLine();

var secondSession = await CreateSessionAsync(http, "B");
if (secondSession == null)
{
    return 5;
}

PrintSessionSummary(secondSession, "Первая sequence новой session");

if (secondSession.Tracks.Count == 0 ||
    secondSession.Tracks.Any(track => string.IsNullOrWhiteSpace(track.Id)))
{
    Console.WriteLine("ОШИБКА: первая sequence теста B не содержит полный набор trackId.");
    return 6;
}

var allTrackIds = secondSession.Tracks
    .Select(track => track.Id!)
    .ToArray();

Console.WriteLine();
Console.WriteLine("Передаём ВСЕ trackId первой sequence:");
Console.WriteLine(JsonSerializer.Serialize(allTrackIds));
Console.WriteLine();

var allQueueResult = await RequestNextSequenceAsync(
    http,
    secondSession.RadioSessionId,
    allTrackIds,
    "B / все trackId первой sequence");

if (allQueueResult == null)
{
    return 7;
}

PrintSessionSummary(allQueueResult, "Sequence после queue из всех 5 trackId");

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ТЕСТ C: НОВАЯ SESSION + НЕСКОЛЬКО TRACK ID");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("Для дополнительной проверки передаём первые 2 trackId");
Console.WriteLine("третьей session.");
Console.WriteLine();

var thirdSession = await CreateSessionAsync(http, "C");
if (thirdSession == null)
{
    return 8;
}

PrintSessionSummary(thirdSession, "Первая sequence третьей session");

var multipleTrackIds = thirdSession.Tracks
    .Where(track => !string.IsNullOrWhiteSpace(track.Id))
    .Take(2)
    .Select(track => track.Id!)
    .ToArray();

if (multipleTrackIds.Length == 0)
{
    Console.WriteLine("ОШИБКА: в тесте C не найдено trackId.");
    return 9;
}

Console.WriteLine();
Console.WriteLine("Передаём несколько trackId:");
Console.WriteLine(JsonSerializer.Serialize(multipleTrackIds));
Console.WriteLine();

var multipleQueueResult = await RequestNextSequenceAsync(
    http,
    thirdSession.RadioSessionId,
    multipleTrackIds,
    "C / несколько trackId");

if (multipleQueueResult == null)
{
    return 10;
}

PrintSessionSummary(multipleQueueResult, "Sequence после queue из нескольких trackId");

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ИТОГ ИССЛЕДОВАНИЯ");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("Проверены три варианта:");
Console.WriteLine("  A. queue: [] в уже существующей session;");
Console.WriteLine("  B. queue со всеми trackId первой sequence в новой session;");
Console.WriteLine("  C. queue с несколькими trackId в новой session.");
Console.WriteLine();
Console.WriteLine("Сравнивать нужно:");
Console.WriteLine("  • состав и порядок рекомендаций;");
Console.WriteLine("  • повторяющиеся треки;");
Console.WriteLine("  • batchId;");
Console.WriteLine("  • terminated;");
Console.WriteLine("  • unknownSession;");
Console.WriteLine("  • влияние количества trackId в queue.");
Console.WriteLine();
Console.WriteLine("Feedback и воспроизведение пока НЕ выполняются.");
Console.WriteLine("Тест завершён.");

return 0;

static async Task<SessionResult?> CreateSessionAsync(
    HttpClient http,
    string testName)
{
    var requestBody = new
    {
        seeds = new[] { station },
        includeTracksInResponse = true
    };

    var requestJson = JsonSerializer.Serialize(requestBody);

    Console.WriteLine($"POST /rotor/session/new [{testName}]");
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
        Console.WriteLine($"ОШИБКА: создание session для теста {testName} не принято.");
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

static async Task<SessionResult?> RequestNextSequenceAsync(
    HttpClient http,
    string radioSessionId,
    IReadOnlyCollection<string> trackIds,
    string testLabel)
{
    Console.WriteLine($"POST /rotor/session/{radioSessionId}/tracks [{testLabel}]");

    var requestBody = new
    {
        queue = trackIds
    };

    var requestJson = JsonSerializer.Serialize(requestBody);

    Console.WriteLine("Body:");
    Console.WriteLine(requestJson);
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
        Console.WriteLine($"ОШИБКА: запрос {testLabel} не принят.");
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
