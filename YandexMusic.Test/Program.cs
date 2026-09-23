using Stream.MusicRequest.Infrastructure.Storage;
using Stream.MusicRequest.Yandex.Services;
using Stream.MusicRequest.Yandex.Sources;
using Stream.MusicRequest.Yandex.Storage;
using YandexMusic;

Console.WriteLine(
    "Тест Yandex Music...");

Console.WriteLine();
Console.WriteLine(
    "Создаём Yandex Music client...");

await using var client =
    new YandexMusicClient();

var storageDirectory =
    Path.Combine(
        Path.GetTempPath(),
        "StreamMusicRequest-YandexAuthServiceTest");

var protectedStorage =
    new DpapiProtectedStorage(
        storageDirectory);

var authStorage =
    new YandexAuthStorage(
        protectedStorage);

var authService =
    new YandexAuthService(
        client,
        authStorage);

Console.WriteLine();
Console.WriteLine(
    "Проверяем восстановление авторизации...");

var restored =
    await authService.TryRestoreAsync();

Console.WriteLine(
    $"Авторизация восстановлена: {restored}");

if (!restored)
{
    Console.WriteLine();
    Console.WriteLine(
        "Сохранённой авторизации нет.");

    Console.WriteLine();
    Console.WriteLine(
        "Запускаем OAuth Device Flow...");

    await authService.AuthorizeAsync(
        (verificationUrl, userCode) =>
        {
            Console.WriteLine();
            Console.WriteLine(
                $"Откройте: {verificationUrl}");

            Console.WriteLine(
                $"Код устройства: {userCode}");

            Console.WriteLine();
            Console.WriteLine(
                "После авторизации программа продолжит работу автоматически.");
        });

    Console.WriteLine();
    Console.WriteLine(
        $"Авторизация выполнена: {authService.IsAuthenticated}");
}
else
{
    Console.WriteLine();
    Console.WriteLine(
        "Используем восстановленную авторизацию.");
}

if (!authService.IsAuthenticated)
{
    Console.WriteLine();
    Console.WriteLine(
        "Ошибка: Yandex Music client не авторизован.");

    return;
}

Console.WriteLine();
Console.WriteLine(
    "Проверяем доступ к Yandex Music...");

var track =
    await client.Tracks.GetAsync(
        "85314452");

if (track == null)
{
    Console.WriteLine(
        "Ошибка: трек не удалось получить.");

    return;
}

Console.WriteLine();
Console.WriteLine(
    "Трек успешно получен:");

Console.WriteLine(
    $"Название: {track.Title}");

Console.WriteLine(
    $"Исполнитель: {track.Artists[0].Name}");

Console.WriteLine(
    $"ID: {track.Id}");

Console.WriteLine();
Console.WriteLine(
    "Создаём YandexMusicSource...");

var source =
    new YandexMusicSource(
        client);

Console.WriteLine();
Console.WriteLine(
    "Проверяем поддержку URL...");

const string playlistUrl =
    "https://music.yandex.ru/playlists/lk.ed6ca488-7a37-4713-9388-67e3355a6c36";

Console.WriteLine(
    $"Плейлист: {source.CanHandlePlaylistUrl(playlistUrl)}");

Console.WriteLine();
Console.WriteLine(
    "Получаем плейлист...");

try
{
    var playlist =
        await source.ResolvePlaylistAsync(
            playlistUrl);

    if (playlist == null)
    {
        Console.WriteLine(
            "Ошибка: плейлист не удалось получить.");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine(
            "Плейлист успешно получен:");

        Console.WriteLine(
            $"Название: {playlist.Title}");

        Console.WriteLine(
            $"Исполнитель: {playlist.Artist ?? "—"}");

        Console.WriteLine(
            $"Обложка: {playlist.CoverUrl ?? "—"}");

        Console.WriteLine(
            $"Количество треков: {playlist.Tracks.Count}");

        Console.WriteLine();

        Console.WriteLine(
            "Первые треки:");

        var count =
            Math.Min(
                playlist.Tracks.Count,
                10);

        for (var i = 0;
             i < count;
             i++)
        {
            var playlistTrack =
                playlist.Tracks[i];

            Console.WriteLine(
                $"{i + 1}. " +
                $"{playlistTrack.Artist} — " +
                $"{playlistTrack.Title} " +
                $"[{playlistTrack.Id}]");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine(
        "Ошибка получения плейлиста:");

    Console.WriteLine(
        ex.Message);
}

Console.WriteLine();
Console.WriteLine(
    "Проверяем поддержку альбома...");

const string albumUrl =
    "https://music.yandex.ru/album/41545061";

Console.WriteLine(
    $"Альбом: {source.CanHandleAlbumUrl(albumUrl)}");

Console.WriteLine();
Console.WriteLine(
    "Тест альбома будет выполнен, " +
    "если указанный URL существует.");

try
{
    var album =
        await source.ResolveAlbumAsync(
            albumUrl);

    if (album == null)
    {
        Console.WriteLine(
            "Альбом не удалось получить " +
            "по тестовому URL.");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine(
            "Альбом успешно получен:");

        Console.WriteLine(
            $"Название: {album.Title}");

        Console.WriteLine(
            $"Исполнитель: {album.Artist ?? "—"}");

        Console.WriteLine(
            $"Обложка: {album.CoverUrl ?? "—"}");

        Console.WriteLine(
            $"Количество треков: {album.Tracks.Count}");

        Console.WriteLine();

        Console.WriteLine(
            "Первые треки:");

        var count =
            Math.Min(
                album.Tracks.Count,
                10);

        for (var i = 0;
             i < count;
             i++)
        {
            var albumTrack =
                album.Tracks[i];

            Console.WriteLine(
                $"{i + 1}. " +
                $"{albumTrack.Artist} — " +
                $"{albumTrack.Title} " +
                $"[{albumTrack.Id}]");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine(
        "Ошибка получения альбома:");

    Console.WriteLine(
        ex.Message);
}

Console.WriteLine();
Console.WriteLine(
    "Проверяем получение прямой ссылки " +
    "на тестовый трек...");

var directLink =
    await client.Tracks.GetDirectLinkAsync(
        track.Id);

Console.WriteLine(
    $"Прямая ссылка получена: " +
    $"{!string.IsNullOrWhiteSpace(directLink)}");

Console.WriteLine();
Console.WriteLine(
    "Очищаем тестовое хранилище...");

await authService.SignOutAsync();

Console.WriteLine(
    $"Авторизация после выхода: " +
    $"{authService.IsAuthenticated}");

Console.WriteLine();
Console.WriteLine(
    "Тест Yandex Music завершён.");