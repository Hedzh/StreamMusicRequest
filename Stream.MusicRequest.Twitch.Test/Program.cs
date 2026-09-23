using Stream.MusicRequest.Twitch.EventSub;

Console.WriteLine(
    "=== Twitch EventSub Test ===");

var client =
    new TwitchEventSubClient();

try
{
    Console.WriteLine();
    Console.WriteLine(
        "Подключение к Twitch EventSub...");

    var session =
        await client.ConnectAsync();

    Console.WriteLine();
    Console.WriteLine(
        "EventSub подключён.");

    Console.WriteLine(
        $"Session ID: {session.Id}");

    Console.WriteLine(
        $"Status: {session.Status}");

    Console.WriteLine(
        $"Connected at: {session.ConnectedAt:O}");

    Console.WriteLine(
        $"Keepalive timeout: " +
        $"{session.KeepaliveTimeoutSeconds} сек.");

    Console.WriteLine();
    Console.WriteLine(
        "Тест подключения завершён.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine(
        "Ошибка:");

    Console.WriteLine(
        ex);

    Environment.ExitCode = 1;
}
finally
{
    await client.DisconnectAsync();
}