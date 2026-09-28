using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Stream.MusicRequest.DonationAlerts.Test.Client;
using Stream.MusicRequest.DonationAlerts.Test.Models;
using Stream.MusicRequest.DonationAlerts.Test.Storage;

Console.OutputEncoding =
    Encoding.UTF8;

Console.WriteLine(
    "==============================================");

Console.WriteLine(
    " Donation Alerts Integration Laboratory");

Console.WriteLine(
    "==============================================");

var settingsPath =
    Path.Combine(
        AppContext.BaseDirectory,
        "donationalerts.testsettings.json");

if (!File.Exists(
        settingsPath))
{
    Console.WriteLine();
    Console.WriteLine(
        "Файл настройки не найден:");

    Console.WriteLine(
        settingsPath);

    Console.WriteLine();
    Console.WriteLine(
        "Скопируй donationalerts.testsettings.json.example");
    Console.WriteLine(
        "в donationalerts.testsettings.json и укажи");
    Console.WriteLine(
        "ClientId и RedirectUri.");

    return;
}

var settings =
    JsonSerializer.Deserialize<DonationAlertsTestSettings>(
        await File.ReadAllTextAsync(
            settingsPath),
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

if (settings == null ||
    string.IsNullOrWhiteSpace(
        settings.ClientId))
{
    Console.WriteLine(
        "В настройках не указан ClientId.");

    return;
}

if (!Uri.TryCreate(
        settings.RedirectUri,
        UriKind.Absolute,
        out var redirectUri) ||
    !string.Equals(
        redirectUri.Scheme,
        "http",
        StringComparison.OrdinalIgnoreCase) ||
    !string.Equals(
        redirectUri.Host,
        "127.0.0.1",
        StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(
        "RedirectUri должен быть локальным HTTP URI вида:");
    Console.WriteLine(
        "http://127.0.0.1:17865/callback/");

    return;
}

Console.WriteLine();
Console.WriteLine(
    $"Redirect URI: {settings.RedirectUri}");

Console.WriteLine();
Console.WriteLine(
    "Введите Client Secret Donation Alerts.");
Console.WriteLine(
    "Он НЕ сохраняется в Git и используется только");
Console.WriteLine(
    "для OAuth/refresh во время этого теста.");

Console.Write(
    "Client Secret: ");

var clientSecret =
    ReadSecret();

if (string.IsNullOrWhiteSpace(
        clientSecret))
{
    Console.WriteLine(
        "Client Secret не указан.");

    return;
}

using var httpClient =
    new HttpClient
    {
        Timeout =
            TimeSpan.FromSeconds(30)
    };

var client =
    new DonationAlertsClient(
        httpClient);

var storage =
    new DonationAlertsTestTokenStorage();

var cancellationSource =
    new CancellationTokenSource();

Console.CancelKeyPress +=
    (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationSource.Cancel();

        Console.WriteLine();
        Console.WriteLine(
            "Останавливаем тест...");
    };

try
{
    var (
        accessToken,
        refreshToken) =
        await storage.LoadAsync();

    if (!string.IsNullOrWhiteSpace(
            accessToken) &&
        !string.IsNullOrWhiteSpace(
            refreshToken))
    {
        Console.WriteLine();
        Console.WriteLine(
            "Сохранённая авторизация Donation Alerts найдена.");

        try
        {
            var existingUser =
                await client.GetCurrentUserAsync(
                    accessToken);

            Console.WriteLine(
                $"Токен действителен. Пользователь: " +
                $"{existingUser.Name} ({existingUser.Code}), ID {existingUser.Id}");

            await RunDonationWebSocketAsync(
                client,
                accessToken,
                existingUser,
                cancellationSource.Token);

            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Сохранённый Access Token не прошёл проверку.");

            Console.WriteLine(
                ex.Message);

            Console.WriteLine();
            Console.WriteLine(
                "Пробуем обновить Access Token через Refresh Token...");

            try
            {
                var refreshed =
                    await client.RefreshTokenAsync(
                        settings.ClientId,
                        clientSecret,
                        refreshToken,
                        settings.Scopes);

                await storage.SaveAsync(
                    refreshed.AccessToken,
                    refreshed.RefreshToken);

                accessToken =
                    refreshed.AccessToken;

                refreshToken =
                    refreshed.RefreshToken;

                Console.WriteLine(
                    "Access Token успешно обновлён.");

                var refreshedUser =
                    await client.GetCurrentUserAsync(
                        accessToken);

                Console.WriteLine(
                    $"Пользователь после refresh: " +
                    $"{refreshedUser.Name} ({refreshedUser.Code}), ID {refreshedUser.Id}");

                await RunDonationWebSocketAsync(
                    client,
                    accessToken,
                    refreshedUser,
                    cancellationSource.Token);

                return;
            }
            catch (Exception refreshException)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Refresh Token также не сработал:");

                Console.WriteLine(
                    refreshException.Message);

                Console.WriteLine();
                Console.WriteLine(
                    "Сбрасываем локальную авторизацию и запускаем OAuth заново.");

                await storage.ClearAsync();
            }
        }
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine(
            "Сохранённой авторизации Donation Alerts нет.");
    }

    var authorizationUrl =
        client.BuildAuthorizationUrl(
            settings.ClientId,
            settings.RedirectUri,
            settings.Scopes);

    Console.WriteLine();
    Console.WriteLine(
        "Запрашиваем OAuth Donation Alerts.");

    Console.WriteLine();
    Console.WriteLine(
        "Будет запрошено:");

    foreach (var scope in settings.Scopes)
    {
        Console.WriteLine(
            $"  - {scope}");
    }

    Console.WriteLine();
    Console.WriteLine(
        "Открываем браузер:");

    Console.WriteLine(
        authorizationUrl);

    Process.Start(
        new ProcessStartInfo
        {
            FileName =
                authorizationUrl,
            UseShellExecute = true
        });

    Console.WriteLine();
    Console.WriteLine(
        "Ожидаем callback от Donation Alerts...");

    var code =
        await WaitForAuthorizationCodeAsync(
            redirectUri,
            cancellationSource.Token);

    Console.WriteLine();
    Console.WriteLine(
        "Authorization Code получен.");

    var token =
        await client.ExchangeCodeAsync(
            settings.ClientId,
            clientSecret,
            settings.RedirectUri,
            code);

    await storage.SaveAsync(
        token.AccessToken,
        token.RefreshToken);

    Console.WriteLine(
        "Access Token и Refresh Token сохранены через DPAPI.");

    Console.WriteLine(
        $"Access Token expires_in: {token.ExpiresIn} сек.");

    var user =
        await client.GetCurrentUserAsync(
            token.AccessToken);

    Console.WriteLine();
    Console.WriteLine(
        "Donation Alerts пользователь:");

    Console.WriteLine(
        $"ID: {user.Id}");

    Console.WriteLine(
        $"Code: {user.Code}");

    Console.WriteLine(
        $"Name: {user.Name}");

    Console.WriteLine(
        $"Socket connection token получен: " +
        $"{(!string.IsNullOrWhiteSpace(user.SocketConnectionToken) ? "ДА" : "НЕТ")}");

    await RunDonationWebSocketAsync(
        client,
        token.AccessToken,
        user,
        cancellationSource.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine(
        "Тест остановлен.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine(
        "========== ОШИБКА ==========");

    Console.WriteLine(
        ex.Message);

    Console.WriteLine();
    Console.WriteLine(
        ex);
}
finally
{
    cancellationSource.Dispose();
}

static async Task RunDonationWebSocketAsync(
    DonationAlertsClient client,
    string accessToken,
    DonationAlertsClient.DonationAlertsUser user,
    CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine(
        "Переходим к тесту real-time Donation Alerts.");

    await client.ConnectToDonationsAsync(
        accessToken,
        user,
        cancellationToken);
}

static async Task<string> WaitForAuthorizationCodeAsync(
    Uri redirectUri,
    CancellationToken cancellationToken)
{
    var port =
        redirectUri.Port;

    var listener =
        new System.Net.Sockets.TcpListener(
            IPAddress.Loopback,
            port);

    listener.Start();

    try
    {
        while (true)
        {
            var tcpClient =
                await listener.AcceptTcpClientAsync(
                    cancellationToken);

            await using var stream =
                tcpClient.GetStream();

            var buffer =
                new byte[16 * 1024];

            var count =
                await stream.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

            var requestText =
                Encoding.ASCII.GetString(
                    buffer,
                    0,
                    count);

            var firstLine =
                requestText
                    .Split(
                        new[]
                        {
                            "\r\n",
                            "\n"
                        },
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(
                    firstLine))
            {
                continue;
            }

            var parts =
                firstLine.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                continue;
            }

            var requestTarget =
                parts[1];

            var callbackUri =
                new Uri(
                    new Uri(
                        $"http://127.0.0.1:{port}"),
                    requestTarget);

            var code =
                GetQueryParameter(
                    callbackUri,
                    "code");

            var error =
                GetQueryParameter(
                    callbackUri,
                    "error");

            var errorDescription =
                GetQueryParameter(
                    callbackUri,
                    "error_description");

            var body =
                string.IsNullOrWhiteSpace(
                    error)
                    ? "<html><body><h2>Donation Alerts authorization received.</h2><p>You can return to the test console.</p></body></html>"
                    : $"<html><body><h2>Donation Alerts authorization failed.</h2><p>{WebUtility.HtmlEncode(error)}</p><p>{WebUtility.HtmlEncode(errorDescription)}</p></body></html>";

            var bodyBytes =
                Encoding.UTF8.GetBytes(
                    body);

            var response =
                $"HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            var responseBytes =
                Encoding.ASCII.GetBytes(
                    response);

            await stream.WriteAsync(
                responseBytes,
                cancellationToken);

            await stream.WriteAsync(
                bodyBytes,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(
                    error))
            {
                throw new InvalidOperationException(
                    $"Donation Alerts OAuth returned error '{error}': {errorDescription}");
            }

            if (!string.IsNullOrWhiteSpace(
                    code))
            {
                return code;
            }
        }
    }
    finally
    {
        listener.Stop();
    }
}

static string? GetQueryParameter(
    Uri uri,
    string name)
{
    var query =
        uri.Query.TrimStart('?');

    foreach (var pair in query.Split(
                 '&',
                 StringSplitOptions.RemoveEmptyEntries))
    {
        var parts =
            pair.Split(
                '=',
                2);

        if (parts.Length != 2)
        {
            continue;
        }

        var key =
            Uri.UnescapeDataString(
                parts[0]);

        if (!string.Equals(
                key,
                name,
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return Uri.UnescapeDataString(
            parts[1]);
    }

    return null;
}

static string ReadSecret()
{
    var builder =
        new StringBuilder();

    while (true)
    {
        var key =
            Console.ReadKey(
                intercept: true);

        if (key.Key ==
            ConsoleKey.Enter)
        {
            Console.WriteLine();
            return builder.ToString();
        }

        if (key.Key ==
            ConsoleKey.Backspace)
        {
            if (builder.Length > 0)
            {
                builder.Length--;

                Console.Write(
                    "\b \b");
            }

            continue;
        }

        if (!char.IsControl(
                key.KeyChar))
        {
            builder.Append(
                key.KeyChar);

            Console.Write(
                '*');
        }
    }
}
