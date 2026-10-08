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
Console.WriteLine(" СОЗДАНИЕ ROTOR SESSION");
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

var radioSessionId = root.TryGetProperty("radioSessionId", out var sessionIdElement)
    ? sessionIdElement.GetString()
    : null;

var batchId = root.TryGetProperty("batchId", out var batchIdElement)
    ? batchIdElement.GetString()
    : null;

if (string.IsNullOrWhiteSpace(radioSessionId))
{
    Console.WriteLine("ОШИБКА: в ответе отсутствует radioSessionId.");
    return 4;
}

Console.WriteLine("==========================================");
Console.WriteLine(" ROTOR SESSION СОЗДАНА");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Radio Session ID: {radioSessionId}");
Console.WriteLine($"Batch ID:         {batchId ?? "не найден"}");
Console.WriteLine();

if (!root.TryGetProperty("sequence", out var sequence) ||
    sequence.ValueKind != JsonValueKind.Array)
{
    Console.WriteLine("В ответе нет sequence.");
    Console.WriteLine("Это не обязательно ошибка: тест запрашивал includeTracksInResponse=true,");
    Console.WriteLine("но сервер мог вернуть сессию без начальной последовательности.");
    Console.WriteLine();
    Console.WriteLine("Первый этап rotor/session завершён успешно.");
    return 0;
}

var tracks = sequence
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

        return new
        {
            Id = id,
            Title = title,
            Artists = artists
        };
    })
    .Where(track => track != null)
    .ToList();

Console.WriteLine($"Получено треков в sequence: {tracks.Count}");
Console.WriteLine();

for (var index = 0; index < tracks.Count; index++)
{
    var track = tracks[index]!;

    Console.WriteLine(
        $"{index + 1}. {track.Artists} — {track.Title} [{track.Id}]");
}

Console.WriteLine();
Console.WriteLine("Первый этап проверки rotor/session завершён.");
Console.WriteLine("Пока feedback, воспроизведение и получение следующей пачки");
Console.WriteLine("намеренно НЕ выполняются — сначала проверяем сам механизм создания сессии.");
return 0;
