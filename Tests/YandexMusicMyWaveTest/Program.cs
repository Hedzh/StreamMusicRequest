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

Console.WriteLine(
    $"Аккаунт: {status?.Account?.DisplayName ?? status?.Account?.Login ?? "не удалось определить"}");

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

var radioFrom = $"mobile-radio-user-{status?.Account?.Uid ?? 0}";

var player = new NAudioMusicPlayer();

try
{
    Console.WriteLine("Первичный radioStarted feedback пропущен для диагностики API.");
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

            var artists = track.Artists.Count > 0
                ? string.Join(", ", track.Artists.Select(a => a.Name))
                : "Неизвестный исполнитель";

            Console.WriteLine(
                $"{i + 1}. {artists} — {track.Title} " +
                $"[{TimeSpan.FromMilliseconds(track.DurationMs):mm\\:ss}]");
        }

        Console.WriteLine();
        Console.Write("Выберите номер трека для воспроизведения (Q — выход): ");

        var input = Console.ReadLine()?.Trim();

        if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (!int.TryParse(input, out var selectedIndex) ||
            selectedIndex < 1 ||
            selectedIndex > batch.Sequence.Count)
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

        var artistsForPlayback = selected.Artists.Count > 0
            ? string.Join(", ", selected.Artists.Select(a => a.Name))
            : "Неизвестный исполнитель";

        Console.WriteLine();
        Console.WriteLine(
            $"Выбран: {artistsForPlayback} — {selected.Title}");

        Console.WriteLine("Получаю прямую ссылку на аудио...");

        var playbackUrl =
            await client.Tracks.GetDirectLinkAsync(selected.Id);

        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            Console.WriteLine("ОШИБКА: прямая ссылка не получена.");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine("Прямая ссылка получена.");
        Console.WriteLine("Начинаю воспроизведение.");
        Console.WriteLine("Enter — пропустить трек, Q — остановить тест.");

        var coreTrack = new Track
        {
            Id = selected.Id,
            Title = selected.Title,
            Artist = artistsForPlayback,
            Duration = TimeSpan.FromMilliseconds(selected.DurationMs),
            SourceUrl = playbackUrl
        };

        var playbackFinished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler? finishedHandler = null;

        finishedHandler = (_, _) =>
        {
            playbackFinished.TrySetResult(true);
        };

        player.PlaybackFinished += finishedHandler;

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            await client.Radio.SendStationFeedbackTrackStartedAsync(
                station,
                selected.Id,
                batch.BatchId);

            await player.PlayAsync(coreTrack);

            while (true)
            {
                if (playbackFinished.Task.IsCompleted)
                {
                    break;
                }

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Enter)
                    {
                        var playedSeconds =
                            Math.Max(
                                0,
                                (DateTimeOffset.UtcNow - startedAt).TotalSeconds);

                        Console.WriteLine();
                        Console.WriteLine("Трек пропущен.");

                        await client.Radio.SendStationFeedbackSkipAsync(
                            station,
                            selected.Id,
                            playedSeconds,
                            batch.BatchId);

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
                var playedSeconds =
                    Math.Max(
                        0,
                        (DateTimeOffset.UtcNow - startedAt).TotalSeconds);

                Console.WriteLine();
                Console.WriteLine("Трек завершён.");

                await client.Radio.SendStationFeedbackTrackFinishedAsync(
                    station,
                    selected.Id,
                    playedSeconds,
                    batch.BatchId);
            }
        }
        finally
        {
            player.PlaybackFinished -= finishedHandler;
        }

        Console.WriteLine();
        Console.WriteLine("Запрашиваю следующую пачку «Моей волны»...");

        var nextBatch =
            await client.Radio.GetStationTracksAsync(
                station,
                queue: selected.Id);

        if (nextBatch == null || nextBatch.Sequence.Count == 0)
        {
            Console.WriteLine("ОШИБКА: следующая пачка не получена.");
            break;
        }

        batch = nextBatch;

        Console.WriteLine(
            $"Новая пачка получена: {batch.Sequence.Count} треков.");
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
