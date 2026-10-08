using Stream.MusicRequest.Audio.Players;
using Stream.MusicRequest.Core.Models;
using YandexMusic;

const string station = "user:onyourwave";

Console.WriteLine("==========================================");
Console.WriteLine(" Stream Music Request — тест «Моя волна»");
Console.WriteLine("==========================================");
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

async Task SendJsonFeedbackAsync(string type, string? trackId = null, double? totalPlayedSeconds = null)
{
    Console.WriteLine($"\n[FEEDBACK] {type}");

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
        return;
    }

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", accessToken);

    var json = System.Text.Json.JsonSerializer.Serialize(fields);
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    var url = $"https://api.music.yandex.net/rotor/station/{Uri.EscapeDataString(station)}/feedback?batch-id={Uri.EscapeDataString(batch.BatchId)}";
    var response = await http.PostAsync(url, content);
    var responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine($"Ответ: {responseText}");
}

var player = new NAudioMusicPlayer();

try
{
    Console.WriteLine("JSON feedback включён для диагностики протокола.");
    Console.WriteLine("Используется авторизованный OAuth-запрос.");
    await SendJsonFeedbackAsync("radioStarted");
    Console.WriteLine();

    while (true)
    {
        Console.WriteLine("Треки текущей пачки:");
        for (var i = 0; i < batch.Sequence.Count; i++)
        {
            var track = batch.Sequence[i].Track;
            if (track == null)
            {
                Console.WriteLine($"{i + 1}. [не трек]");
                continue;
            }

            var artists = track.Artists.Count > 0 ? string.Join(", ", track.Artists.Select(a => a.Name)) : "Неизвестный исполнитель";
            Console.WriteLine($"{i + 1}. {artists} — {track.Title} [{(int)TimeSpan.FromMilliseconds(track.DurationMs).TotalMinutes:00}:{TimeSpan.FromMilliseconds(track.DurationMs).Seconds:00}]");
        }

        Console.WriteLine();
        Console.Write("Выберите номер трека для воспроизведения (Q — выход): ");
        var input = Console.ReadLine()?.Trim();
        if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase)) break;
        if (!int.TryParse(input, out var selectedIndex) || selectedIndex < 1 || selectedIndex > batch.Sequence.Count)
        {
            Console.WriteLine("Некорректный номер.");
            Console.WriteLine();
            continue;
        }

        var selected = batch.Sequence[selectedIndex - 1].Track;
        if (selected == null)
        {
            Console.WriteLine("Выбранный элемент не является треком.");
            Console.WriteLine();
            continue;
        }

        var artistsForPlayback = selected.Artists.Count > 0 ? string.Join(", ", selected.Artists.Select(a => a.Name)) : "Неизвестный исполнитель";
        Console.WriteLine();
        Console.WriteLine($"Выбран: {artistsForPlayback} — {selected.Title}");
        await SendJsonFeedbackAsync("trackStarted", selected.Id);

        Console.WriteLine("Получаю прямую ссылку на аудио...");
        var playbackUrl = await client.Tracks.GetDirectLinkAsync(selected.Id);
        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            Console.WriteLine("ОШИБКА: прямая ссылка не получена.");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine("Прямая ссылка получена.");
        Console.WriteLine("Начинаю воспроизведение.");
        Console.WriteLine("Enter — пропустить трек, Q — остановить тест.");

        var coreTrack = new Track { Id = selected.Id, Title = selected.Title, Artist = artistsForPlayback, Duration = TimeSpan.FromMilliseconds(selected.DurationMs), SourceUrl = playbackUrl };
        var playbackFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? finishedHandler = (_, _) => playbackFinished.TrySetResult(true);
        player.PlaybackFinished += finishedHandler;

        try
        {
            await player.PlayAsync(coreTrack);
            while (!playbackFinished.Task.IsCompleted)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Трек пропущен.");
                        await SendJsonFeedbackAsync("skip", selected.Id, 0.1);
                        await player.StopAsync();
                        break;
                    }
                    if (key.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine();
                        await player.StopAsync();
                        return 0;
                    }
                }
                await Task.Delay(100);
            }

            if (playbackFinished.Task.IsCompleted)
            {
                Console.WriteLine();
                Console.WriteLine("Трек завершён.");
                await SendJsonFeedbackAsync("trackFinished", selected.Id, selected.DurationMs / 1000.0);
            }
        }
        finally
        {
            player.PlaybackFinished -= finishedHandler;
        }

        Console.WriteLine();
        Console.WriteLine("Запрашиваю следующую пачку «Моей волны»...");
        var nextBatch = await client.Radio.GetStationTracksAsync(station, queue: selected.Id);
        if (nextBatch == null || nextBatch.Sequence.Count == 0)
        {
            Console.WriteLine("ОШИБКА: следующая пачка не получена.");
            break;
        }
        batch = nextBatch;
        Console.WriteLine($"Новая пачка получена: {batch.Sequence.Count} треков.");
        Console.WriteLine();
    }
}
finally
{
    await player.DisposeAsync();
}

Console.WriteLine();
Console.WriteLine("Тест завершён.");
return 0;
