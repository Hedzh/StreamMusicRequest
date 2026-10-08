using Stream.MusicRequest.Audio.Players;
using Stream.MusicRequest.Core.Models;
using YandexMusic;

const string station = "user:onyourwave";
const int testTrackSeconds = 10;
const int testTracks = 3;

Console.WriteLine("==========================================");
Console.WriteLine(" Stream Music Request — тест «Моя волна»");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine($"Автоматический режим: {testTracks} трека по {testTrackSeconds} секунд.");
Console.WriteLine("Тест специально не ждёт полного окончания треков.");
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

Console.WriteLine();
Console.WriteLine("Авторизация успешна.");
Console.WriteLine();

var status = await client.Account.GetStatusAsync();
Console.WriteLine($"Аккаунт: {status?.Account?.DisplayName ?? status?.Account?.Login ?? "не удалось определить"}");

Console.WriteLine();
Console.WriteLine("Получаю первую пачку «Моей волны»...");

var batch = await client.Radio.GetStationTracksAsync(station);

if (batch == null || batch.Sequence.Count == 0)
{
    Console.WriteLine("ОШИБКА: Yandex Music не вернул треки.");
    return 2;
}

Console.WriteLine($"Получено треков: {batch.Sequence.Count}");
Console.WriteLine($"Batch ID: {batch.BatchId}");
Console.WriteLine();

async Task<bool> SendJsonFeedbackAsync(string type, string? trackId = null, double? totalPlayedSeconds = null)
{
    Console.WriteLine($"[FEEDBACK] {type}");

    var fields = new Dictionary<string, object?>
    {
        ["type"] = type,
        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    if (!string.IsNullOrWhiteSpace(trackId))
        fields["trackId"] = trackId;

    if (totalPlayedSeconds is not null)
        fields["totalPlayedSeconds"] = totalPlayedSeconds;

    var accessToken = token ?? client.Authentication.Session.AccessToken;

    if (string.IsNullOrWhiteSpace(accessToken))
    {
        Console.WriteLine("ОШИБКА: OAuth-токен для JSON feedback не найден.");
        return false;
    }

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", accessToken);

    var json = System.Text.Json.JsonSerializer.Serialize(fields);
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    var url =
        $"https://api.music.yandex.net/rotor/station/{Uri.EscapeDataString(station)}/feedback" +
        $"?batch-id={Uri.EscapeDataString(batch.BatchId)}";

    var response = await http.PostAsync(url, content);
    var responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"  HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine($"  Ответ: {responseText}");

    return response.IsSuccessStatusCode &&
           responseText.Contains("\"result\":\"ok\"", StringComparison.OrdinalIgnoreCase);
}

var player = new NAudioMusicPlayer();

try
{
    Console.WriteLine("JSON feedback включён для диагностики протокола.");
    Console.WriteLine("Используется авторизованный OAuth-запрос.");
    Console.WriteLine();

    await SendJsonFeedbackAsync("radioStarted");
    Console.WriteLine();

    string? lastProcessedTrackId = null;

    for (var testIndex = 1; testIndex <= testTracks; testIndex++)
    {
        Console.WriteLine($"========== ТЕСТОВЫЙ ТРЕК {testIndex}/{testTracks} ==========");

        if (batch.Sequence.Count == 0)
        {
            Console.WriteLine("ОШИБКА: текущая пачка пуста.");
            break;
        }

        var sequenceItem = batch.Sequence.FirstOrDefault(x => x.Track != null && x.Track.Id != lastProcessedTrackId)
                           ?? batch.Sequence.FirstOrDefault(x => x.Track != null);

        var selected = sequenceItem?.Track;
        if (selected == null)
        {
            Console.WriteLine("ОШИБКА: в пачке нет пригодного трека.");
            break;
        }

        lastProcessedTrackId = selected.Id;

        var artists = selected.Artists.Count > 0
            ? string.Join(", ", selected.Artists.Select(a => a.Name))
            : "Неизвестный исполнитель";

        Console.WriteLine($"Выбран: {artists} — {selected.Title}");
        Console.WriteLine($"Длительность: {TimeSpan.FromMilliseconds(selected.DurationMs):mm\\:ss}");
        Console.WriteLine();

        if (!await SendJsonFeedbackAsync("trackStarted", selected.Id))
        {
            Console.WriteLine("ВНИМАНИЕ: trackStarted не подтверждён.");
        }

        Console.WriteLine("Получаю прямую ссылку на аудио...");
        var playbackUrl = await client.Tracks.GetDirectLinkAsync(selected.Id);

        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            Console.WriteLine("ОШИБКА: прямая ссылка не получена.");
            break;
        }

        Console.WriteLine("Прямая ссылка получена.");
        Console.WriteLine($"Начинаю воспроизведение на {testTrackSeconds} секунд.");

        var coreTrack = new Track
        {
            Id = selected.Id,
            Title = selected.Title,
            Artist = artists,
            Duration = TimeSpan.FromMilliseconds(selected.DurationMs),
            SourceUrl = playbackUrl
        };

        await player.PlayAsync(coreTrack);

        var playbackStartedAt = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromSeconds(testTrackSeconds));
        var playedSeconds = Math.Max(
            0.1,
            (DateTimeOffset.UtcNow - playbackStartedAt).TotalSeconds);

        await player.StopAsync();

        Console.WriteLine($"Тестовое воспроизведение остановлено после {playedSeconds:F1} сек.");
        Console.WriteLine("Отправляю trackFinished...");

        if (!await SendJsonFeedbackAsync("trackFinished", selected.Id, playedSeconds))
        {
            Console.WriteLine("ВНИМАНИЕ: trackFinished не подтверждён.");
        }

        Console.WriteLine();

        if (testIndex >= testTracks)
            break;

        Console.WriteLine("Запрашиваю следующую пачку «Моей волны»...");
        var nextBatch = await client.Radio.GetStationTracksAsync(
            station,
            queue: selected.Id);

        if (nextBatch == null || nextBatch.Sequence.Count == 0)
        {
            Console.WriteLine("ОШИБКА: следующая пачка не получена.");
            break;
        }

        batch = nextBatch;

        Console.WriteLine($"Новая пачка получена: {batch.Sequence.Count} треков.");
        Console.WriteLine($"Новый Batch ID: {batch.BatchId}");
        Console.WriteLine();
    }
}
finally
{
    await player.DisposeAsync();
}

Console.WriteLine();
Console.WriteLine("Автоматический тест «Моей волны» завершён.");
return 0;
