using Stream.MusicRequest.Twitch.EventSub;

namespace Stream.MusicRequest.Twitch.Test;

public static class TwitchEventSubTest
{
    public static async Task RunAsync()
    {
        var client =
            new TwitchEventSubClient();

        using var cancellation =
            new CancellationTokenSource();

        try
        {
            Console.WriteLine(
                "Подключение к Twitch EventSub...");

            var sessionId =
                await client.ConnectAsync(
                    cancellation.Token);

            Console.WriteLine(
                "Twitch EventSub подключён.");

            Console.WriteLine(
                $"Session ID: {sessionId}");

            Console.WriteLine(
                "Ожидание событий...");

            await Task.Delay(
                Timeout.Infinite,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                "Тестовое подключение завершено.");
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }
}