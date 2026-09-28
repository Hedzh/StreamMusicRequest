using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Stream.MusicRequest.DonationAlerts.Test.Client;
using Stream.MusicRequest.DonationAlerts.Test.Models;
using Stream.MusicRequest.DonationAlerts.Test.Storage;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("==============================================");
Console.WriteLine(" Donation Alerts Integration Laboratory");
Console.WriteLine("==============================================");

var settingsPath =
    Path.Combine(
        AppContext.BaseDirectory,
        "donationalerts.testsettings.json");

if (!File.Exists(settingsPath))
{
    Console.WriteLine();
    Console.WriteLine("Файл настройки не найден:");
    Console.WriteLine(settingsPath);
    Console.WriteLine();
    Console.WriteLine("Скопируй donationalerts.testsettings.json.example");
    Console.WriteLine("в donationalerts.testsettings.json и укажи");
    Console.WriteLine("ClientId и RedirectUri.");
    return;
}

var settings =
    JsonSerializer.Deserialize<DonationAlertsTestSettings>(
        await File.ReadAllTextAsync(settingsPath),
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

if (settings == null ||
    string.IsNullOrWhiteSpace(settings.ClientId))
{
    Console.WriteLine("В настройках не указан ClientId.");
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
    Console.WriteLine("http://127.0.0.1:17865/callback/");
    return;
}

Console.WriteLine();
Console.WriteLine($"Redirect URI: {settings.RedirectUri}");
Console.WriteLine();
Console.WriteLine("Donation Alerts используется только через Implicit Grant.");
Console.WriteLine("Client Secret не требуется и не используется.");
Console.WriteLine();

using var httpClient =
    new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

var client =
    new DonationAlertsClient(httpClient);

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
        Console.WriteLine("Останавливаем тест...");
    };

try
{
    var accessToken =
        await storage.LoadAsync();

    if (!string.IsNullOrWhiteSpace(accessToken))
    {
        Console.WriteLine(
            "Сохранённая Implicit Grant авторизация найдена.");

        try
        {
            var savedUser =
                await client.GetCurrentUserAsync(
                    accessToken);

            Console.WriteLine(
                $"Сохранённый Access Token действителен.");
            Console.WriteLine(
                $"Пользователь: {savedUser.Name} ({savedUser.Code}), ID {savedUser.Id}");

            await RunDonationWebSocketAsync(
                client,
                accessToken,
                savedUser,
                cancellationSource.Token);

            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Сохранённый Access Token не прошёл проверку.");
            Console.WriteLine(ex.Message);
            Console.WriteLine();
            Console.WriteLine(
                "Локальная авторизация будет сброшена.");
            
            await storage.ClearAsync();
        }
    }
    else
    {
        Console.WriteLine(
            "Сохранённой Implicit Grant авторизации нет.");
    }

    await AuthorizeAndRunAsync(
        client,
        storage,
        settings,
        redirectUri,
        cancellationSource.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("Тест остановлен.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("========== ОШИБКА ==========");
    Console.WriteLine(ex.Message);
    Console.WriteLine();
    Console.WriteLine(ex);
}
finally
{
    cancellationSource.Dispose();
}

static async Task AuthorizeAndRunAsync(
    DonationAlertsClient client,
    DonationAlertsTestTokenStorage storage,
    DonationAlertsTestSettings settings,
    Uri redirectUri,
    CancellationToken cancellationToken)
{
    var authorizationUrl =
        client.BuildAuthorizationUrl(
            settings.ClientId,
            settings.RedirectUri,
            settings.Scopes);

    Console.WriteLine();
    Console.WriteLine(
        "Запрашиваем OAuth Donation Alerts через Implicit Grant.");

    Console.WriteLine();
    Console.WriteLine("Будет запрошено:");

    foreach (var scope in settings.Scopes)
    {
        Console.WriteLine($"  - {scope}");
    }

    Console.WriteLine();
    Console.WriteLine("Открываем браузер:");
    Console.WriteLine(authorizationUrl);

    Process.Start(
        new ProcessStartInfo
        {
            FileName = authorizationUrl,
            UseShellExecute = true
        });

    Console.WriteLine();
    Console.WriteLine(
        "Ожидаем возврат из браузера и получение Access Token...");

    var accessToken =
        await WaitForImplicitAccessTokenAsync(
            redirectUri,
            cancellationToken);

    Console.WriteLine();
    Console.WriteLine(
        "Access Token получен через Implicit Grant.");

    Console.WriteLine(
        "Сохраняем Access Token через DPAPI.");

    await storage.SaveAsync(accessToken);

    Console.WriteLine(
        "Access Token сохранён.");

    var user =
        await client.GetCurrentUserAsync(
            accessToken);

    PrintUser(user);

    await RunDonationWebSocketAsync(
        client,
        accessToken,
        user,
        cancellationToken);
}

static void PrintUser(
    DonationAlertsClient.DonationAlertsUser user)
{
    Console.WriteLine();
    Console.WriteLine("Donation Alerts пользователь:");
    Console.WriteLine($"ID: {user.Id}");
    Console.WriteLine($"Code: {user.Code}");
    Console.WriteLine($"Name: {user.Name}");
    Console.WriteLine(
        $"Socket connection token получен: " +
        $"{(!string.IsNullOrWhiteSpace(user.SocketConnectionToken) ? "ДА" : "НЕТ")}");
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

static async Task<string> WaitForImplicitAccessTokenAsync(
    Uri redirectUri,
    CancellationToken cancellationToken)
{
    var listener =
        new System.Net.Sockets.TcpListener(
            IPAddress.Loopback,
            redirectUri.Port);

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
                Encoding.UTF8.GetString(
                    buffer,
                    0,
                    count);

            var firstLine =
                requestText
                    .Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstLine))
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

            var requestTarget = parts[1];

            if (requestText.StartsWith(
                    "POST ",
                    StringComparison.OrdinalIgnoreCase))
            {
                var separator =
                    requestText.IndexOf(
                        "\r\n\r\n",
                        StringComparison.Ordinal);

                if (separator < 0)
                {
                    continue;
                }

                var formBody =
                    requestText[(separator + 4)..];

                var token =
                    GetFormParameter(
                        formBody,
                        "access_token");

                var error =
                    GetFormParameter(
                        formBody,
                        "error");

                var errorDescription =
                    GetFormParameter(
                        formBody,
                        "error_description");

                await WriteHttpResponseAsync(
                    stream,
                    "<html><body><h2>Готово</h2><p>Вернитесь в окно тестовой программы.</p></body></html>",
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidOperationException(
                        $"Donation Alerts OAuth returned error '{error}': {errorDescription}");
                }

                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }

                continue;
            }

            if (!requestTarget.StartsWith(
                    "/callback",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var body =
                """
                <!doctype html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <title>Donation Alerts</title>
                </head>
                <body>
                    <h2>Donation Alerts authorization</h2>
                    <p>Передача результата авторизации тестовой программе...</p>
                    <script>
                    (() => {
                        const hash = window.location.hash.substring(1);
                        const params = new URLSearchParams(hash);
                        const token = params.get('access_token');
                        const error = params.get('error');
                        const errorDescription = params.get('error_description');

                        const form = new URLSearchParams();

                        if (token) form.set('access_token', token);
                        if (error) form.set('error', error);
                        if (errorDescription) form.set('error_description', errorDescription);

                        fetch('/callback', {
                            method: 'POST',
                            headers: {'Content-Type': 'application/x-www-form-urlencoded'},
                            body: form.toString()
                        }).then(() => {
                            document.body.innerHTML =
                                '<h2>Готово</h2><p>Вернитесь в окно тестовой программы.</p>';
                        }).catch(() => {
                            document.body.innerHTML =
                                '<h2>Ошибка</h2><p>Не удалось передать результат авторизации.</p>';
                        });
                    })();
                    </script>
                </body>
                </html>
                """;

            await WriteHttpResponseAsync(
                stream,
                body,
                cancellationToken);
        }
    }
    finally
    {
        listener.Stop();
    }
}

static async Task WriteHttpResponseAsync(
    System.Net.Sockets.NetworkStream stream,
    string body,
    CancellationToken cancellationToken)
{
    var bodyBytes =
        Encoding.UTF8.GetBytes(body);

    var response =
        $"HTTP/1.1 200 OK\r\n" +
        "Content-Type: text/html; charset=utf-8\r\n" +
        $"Content-Length: {bodyBytes.Length}\r\n" +
        "Connection: close\r\n" +
        "\r\n";

    await stream.WriteAsync(
        Encoding.ASCII.GetBytes(response),
        cancellationToken);

    await stream.WriteAsync(
        bodyBytes,
        cancellationToken);
}

static string? GetFormParameter(
    string form,
    string name)
{
    foreach (var pair in form.Split(
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
                parts[0].Replace("+", " "));

        if (!string.Equals(
                key,
                name,
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return Uri.UnescapeDataString(
            parts[1].Replace("+", " "));
    }

    return null;
}
