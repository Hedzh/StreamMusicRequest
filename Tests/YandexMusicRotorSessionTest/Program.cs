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

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ЭТАП 1: СОЗДАНИЕ ROTOR SESSION");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Seed: {station}");
Console.WriteLine("includeTracksInResponse: true");
Console.WriteLine();

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("OAuth", accessToken);

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

Console.WriteLine("==========================================");
Console.WriteLine(" ROTOR SESSION СОЗДАНА");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Radio Session ID: {radioSessionId}");
Console.WriteLine($"Batch ID:         {batchId ?? "не найден"}");
Console.WriteLine();

if (!result.TryGetProperty("sequence", out var sequence) ||
    sequence.ValueKind != JsonValueKind.Array)
{
    Console.WriteLine("В result нет sequence.");
    Console.WriteLine("Тест продолжения невозможно выполнить без начальной последовательности.");
    return 6;
}

var tracks = ParseTracks(sequence);

Console.WriteLine($"Получено треков в первой sequence: {tracks.Count}");
Console.WriteLine();

PrintTracks(tracks);

if (tracks.Count == 0 || string.IsNullOrWhiteSpace(tracks[0].Id))
{
    Console.WriteLine("ОШИБКА: первая sequence не содержит пригодного trackId.");
    return 7;
}

var firstTrackId = tracks[0].Id!;

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" ЭТАП 2: ПРОДОЛЖЕНИЕ ТОЙ ЖЕ ROTOR SESSION");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("Исследуем подтверждённый в сторонних реализациях запрос:");
Console.WriteLine($"POST /rotor/session/{radioSessionId}/tracks");
Console.WriteLine("Body:");
Console.WriteLine($"{{ "queue": ["{firstTrackId}"] }}");
Console.WriteLine();
Console.WriteLine("Важно: это НЕ создание новой сессии.");
Console.WriteLine("Используется тот же radioSessionId.");
Console.WriteLine();

var nextRequestBody = new
{
    queue = new[] { firstTrackId }
};

var nextRequestJson = JsonSerializer.Serialize(nextRequestBody);

using var nextContent = new StringContent(
    nextRequestJson,
    Encoding.UTF8,
    "application/json");

using var nextResponse = await http.PostAsync(
    $"{apiBaseUrl}/rotor/session/{Uri.EscapeDataString(radioSessionId)}/tracks",
    nextContent);

var nextResponseText = await nextResponse.Content.ReadAsStringAsync();

Console.WriteLine($"HTTP {(int)nextResponse.StatusCode} {nextResponse.StatusCode}");
Console.WriteLine("Ответ:");
Console.WriteLine(nextResponseText);
Console.WriteLine();

if (!nextResponse.IsSuccessStatusCode)
{
    Console.WriteLine("ОШИБКА: rotor/session/{id}/tracks не принял запрос.");
    return 8;
}

using var nextJson = JsonDocument.Parse(nextResponseText);
var nextRoot = nextJson.RootElement;

if (!nextRoot.TryGetProperty("result", out var nextResult) ||
    nextResult.ValueKind != JsonValueKind.Object)
{
    Console.WriteLine("ОШИБКА: в ответе продолжения отсутствует объект result.");
    return 9;
}

if (!nextResult.TryGetProperty("sequence", out var nextSequence) ||
    nextSequence.ValueKind != JsonValueKind.Array)
{
    Console.WriteLine("ОШИБКА: в ответе продолжения отсутствует sequence.");
    return 10;
}

var nextTracks = ParseTracks(nextSequence);

Console.WriteLine("==========================================");
Console.WriteLine(" ПРОДОЛЖЕНИЕ ROTOR SESSION ПОЛУЧЕНО");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Получено треков во второй sequence: {nextTracks.Count}");
Console.WriteLine();

PrintTracks(nextTracks);

var nextBatchId = nextResult.TryGetProperty("batchId", out var nextBatchElement)
    ? nextBatchElement.GetString()
    : null;

Console.WriteLine();
Console.WriteLine($"Batch ID первой sequence:  {batchId ?? "не найден"}");
Console.WriteLine($"Batch ID второй sequence:  {nextBatchId ?? "не найден"}");
Console.WriteLine($"Radio Session ID сохранён: {radioSessionId}");
Console.WriteLine();

Console.WriteLine("Этапы 1–2 завершены.");
Console.WriteLine("Создание сессии и получение следующей sequence внутри той же");
Console.WriteLine("rotor session подтверждены.");
Console.WriteLine();
Console.WriteLine("Feedback и воспроизведение пока НЕ выполняются.");
return 0;

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
