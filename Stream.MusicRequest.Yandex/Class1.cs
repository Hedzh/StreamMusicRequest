using YandexMusic;

namespace Stream.MusicRequest.Yandex;

public class YandexMusicTest
{
    public static async Task RunAsync()
    {
        await using var client = new YandexMusicClient();

        Console.WriteLine("YandexMusicClient успешно создан.");
    }
}