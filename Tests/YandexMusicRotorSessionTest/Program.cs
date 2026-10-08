using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YandexMusic;

const string station = "user:onyourwave";
const string apiBaseUrl = "https://api.music.yandex.net";

Console.WriteLine("====================================================");
Console.WriteLine(" Stream Music Request — тест Yandex Music rotor/session");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("Это отдельный диагностический тест.");
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
Console.WriteLine(" ЭТАП 1: СОЗДАНИЕ ROTOR SESSION");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Seed: {station}");
Console.WriteLine("includeTracksInResponse: true");
Console.WriteLine();

var requestBody = new
{
    seeds = new[] { station },
    includeTracksInResponse = true
};

var requestJson = JsonSerializer.Serialize(requestBody);

Console.WriteLine("POST /rotor/session/new");
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
    Console.WriteLine("ОШИБКА: rotor/session/new не принял запрос.");
    return 3;
}

using var json = JsonDocument.Parse(responseText);
var root = json.RootElement;

if (!root.TryGetProperty("result", out var result) ||
    result.ValueKind != JsonValueKind.Object)
{
    Console.WriteLine("ОШИБКА: в ответе отсутствует объект result.");
    return 4;
}

var radioSessionId = result.TryGetProperty("radioSessionId", out var sessionIdElement)
    ? sessionIdElement.GetString()
    : null;

var batchId = result.TryGetProperty("batchId", out var batchIdElement)
    ? batchIdElement.GetString()
    : null;

if (string.IsNullOrWhiteSpace(radioSessionId))
{
    Console.WriteLine("ОШИБКА: в result отсутствует radioSessionId.");
    return 5;
}

if (!result.TryGetProperty("sequence", out var sequence) ||
    sequence.ValueKind != JsonValueKind.Array)
{
    Console.WriteLine("В result нет sequence.");
    return 6;
}

var firstTracks = ParseTracks(sequence);

Console.WriteLine("==========================================");
Console.WriteLine(" ROTOR SESSION СОЗДАНА");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Radio Session ID: {radioSessionId}");
Console.WriteLine($"Batch ID:         {batchId ?? "не найден"}");
Console.WriteLine($"Получено треков в первой sequence: {firstTracks.Count}");
Console.WriteLine();

PrintTracks(firstTracks);

if (firstTracks.Count == 0 || string.IsNullOrWhiteSpace(firstTracks[^1].Id))
{
    Console.WriteLine("ОШИБКА: первая sequence не содержит пригодного последнего trackId.");
    return 7;
}

Console.WriteLine();
Console.WriteLine("Для следующего запроса будет использован ПОСЛЕДНИЙ трек первой sequence.");
Console.WriteLine($"Queue trackId: {firstTracks[^1].Id}");

var secondResult = await RequestNextSequenceAsync(
    http,
    radioSessionId,
    firstTracks[^1].Id!,
    2);

if (secondResult == null)
{
    return 8;
}

var secondTracks = secondResult.Tracks;
var secondBatchId = secondResult.BatchId;

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ЭТАП 2: ВТОРАЯ SEQUENCE ТОЙ ЖЕ SESSION");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Radio Session ID: {radioSessionId}");
Console.WriteLine($"Batch ID второй sequence: {secondBatchId ?? "не найден"}");
Console.WriteLine($"Получено треков: {secondTracks.Count}");
Console.WriteLine();

PrintTracks(secondTracks);

if (secondTracks.Count == 0 || string.IsNullOrWhiteSpace(secondTracks[^1].Id))
{
    Console.WriteLine("ОШИБКА: вторая sequence не содержит пригодного последнего trackId.");
    return 9;
}

Console.WriteLine();
Console.WriteLine("Для следующего запроса будет использован ПОСЛЕДНИЙ трек второй sequence.");
Console.WriteLine($"Queue trackId: {secondTracks[^1].Id}");

var thirdResult = await RequestNextSequenceAsync(
    http,
    radioSessionId,
    secondTracks[^1].Id!,
    3);

if (thirdResult == null)
{
    return 10;
}

var thirdTracks = thirdResult.Tracks;
var thirdBatchId = thirdResult.BatchId;

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ЭТАП 3: ТРЕТЬЯ SEQUENCE ТОЙ ЖЕ SESSION");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Radio Session ID: {radioSessionId}");
Console.WriteLine($"Batch ID третьей sequence: {thirdBatchId ?? "не найден"}");
Console.WriteLine($"Получено треков: {thirdTracks.Count}");
Console.WriteLine();

PrintTracks(thirdTracks);

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ИТОГ ИССЛЕДОВАНИЯ");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Radio Session ID один и тот же: {radioSessionId}");
Console.WriteLine($"Batch ID #1: {batchId ?? "не найден"}");
Console.WriteLine($"Batch ID #2: {secondBatchId ?? "не найден"}");
Console.WriteLine($"Batch ID #3: {thirdBatchId ?? "не найден"}");
Console.WriteLine();
Console.WriteLine("Сравните последовательности на:");
Console.WriteLine("  • повторяющиеся треки;");
Console.WriteLine("  • изменение рекомендаций;");
Console.WriteLine("  • изменение batchId;");
Console.WriteLine("  • наличие terminated/unknownSession в ответах;");
Console.WriteLine("  • влияние передачи ПОСЛЕДНЕГО trackId предыдущей sequence.");
Console.WriteLine();
Console.WriteLine("Feedback и воспроизведение пока НЕ выполняются.");
Console.WriteLine("Тест завершён.");

return 0;

static async Task<SequenceResult?> RequestNextSequenceAsync(
    HttpClient http,
    string radioSessionId,
    string trackId,
    int stage)
{
    Console.WriteLine();
    Console.WriteLine("==========================================");
    Console.WriteLine($" ЭТАП {stage}: ПРОДОЛЖЕНИЕ ROTOR SESSION");
    Console.WriteLine("==========================================");
    Console.WriteLine();
    Console.WriteLine($"POST /rotor/session/{radioSessionId}/tracks");
    Console.WriteLine("Body:");
    Console.WriteLine(JsonSerializer.Serialize(new { queue = new[] { trackId } }));
    Console.WriteLine();
    Console.WriteLine($"Используем последний trackId предыдущей sequence: {trackId}");
    Console.WriteLine();

    var requestBody = new
    {
        queue = new[] { trackId }
    };

    var requestJson = JsonSerializer.Serialize(requestBody);

    using var content = new StringContent(
        requestJson,
        Encoding.UTF8,
        "application/json");

    using var response = await http.PostAsync(
        $"https://api.music.yandex.net/rotor/session/{Uri.EscapeDataString(radioSessionId)}/tracks",
        content);

    var responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine("Ответ:");
    Console.WriteLine(responseText);
    Console.WriteLine();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"ОШИБКА: продолжение rotor session на этапе {stage} не принято.");
        return null;
    }

    using var json = JsonDocument.Parse(responseText);
    var root = json.RootElement;

    if (!root.TryGetProperty("result", out var result) ||
        result.ValueKind != JsonValueKind.Object)
    {
        Console.WriteLine("ОШИБКА: в ответе продолжения отсутствует объект result.");
        return null;
    }

    if (!result.TryGetProperty("sequence", out var sequence) ||
        sequence.ValueKind != JsonValueKind.Array)
    {
        Console.WriteLine("ОШИБКА: в ответе продолжения отсутствует sequence.");
        return null;
    }

    var tracks = ParseTracks(sequence);

    var batchId = result.TryGetProperty("batchId", out var batchElement)
        ? batchElement.GetString()
        : null;

    var terminated = result.TryGetProperty("terminated", out var terminatedElement) &&
                     terminatedElement.ValueKind == JsonValueKind.True;

    var unknownSession = result.TryGetProperty("unknownSession", out var unknownSessionElement) &&
                         unknownSessionElement.ValueKind == JsonValueKind.True;

    Console.WriteLine($"Разобрано треков: {tracks.Count}");
    Console.WriteLine($"terminated: {terminated}");
    Console.WriteLine($"unknownSession: {unknownSession}");
    Console.WriteLine($"batchId: {batchId ?? "не найден"}");

    return new SequenceResult(tracks, batchId);
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

record SequenceResult(List<TrackInfo> Tracks, string? BatchId);
