using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stream.MusicRequest.DonationAlerts.Test.Client;

public sealed class DonationAlertsClient
{
    private const string ApiBaseUrl =
        "https://www.donationalerts.com/api/v1";

    private const string OAuthAuthorizeUrl =
        "https://www.donationalerts.com/oauth/authorize";

    private const string CentrifugoUrl =
        "wss://centrifugo.donationalerts.com/connection/websocket";

    private readonly HttpClient _httpClient;

    public DonationAlertsClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string BuildAuthorizationUrl(
        string clientId,
        string redirectUri,
        IEnumerable<string> scopes)
    {
        var query =
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "token",
                ["scope"] = string.Join(" ", scopes)
            };

        return OAuthAuthorizeUrl +
               "?" +
               string.Join(
                   "&",
                   query.Select(
                       pair =>
                           $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    public async Task<DonationAlertsUser> GetCurrentUserAsync(
        string accessToken)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                $"{ApiBaseUrl}/user/oauth",
                accessToken);

        var response =
            await SendJsonAsync<DonationAlertsDataResponse<DonationAlertsUser>>(
                request);

        if (response.Data == null)
        {
            throw new InvalidOperationException(
                "Donation Alerts /user/oauth не вернул поле data.");
        }

        return response.Data;
    }

    public async Task ConnectToDonationsAsync(
        string accessToken,
        DonationAlertsUser user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                user.SocketConnectionToken))
        {
            throw new InvalidOperationException(
                "Donation Alerts не вернул socket_connection_token.");
        }

        using var socket =
            new ClientWebSocket();

        socket.Options.KeepAliveInterval =
            TimeSpan.FromSeconds(20);

        Console.WriteLine();
        Console.WriteLine(
            "Подключаемся к Donation Alerts Centrifugo...");

        await socket.ConnectAsync(
            new Uri(CentrifugoUrl),
            cancellationToken);

        Console.WriteLine(
            "WebSocket соединение установлено.");

        var connectMessage =
            JsonSerializer.Serialize(
                new
                {
                    @params = new
                    {
                        token =
                            user.SocketConnectionToken
                    },
                    id = 1
                });

        await SendWebSocketMessageAsync(
            socket,
            connectMessage,
            cancellationToken);

        var connectResponse =
            await ReceiveWebSocketMessageAsync(
                socket,
                cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "Ответ Centrifugo на подключение:");

        PrintRawJson(connectResponse);

        var clientId =
            ExtractClientId(connectResponse);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Donation Alerts не вернул Centrifugo Client ID.");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Centrifugo Client ID: {clientId}");

        var subscription =
            await SubscribeAsync(
                accessToken,
                clientId,
                user.Id,
                cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "Ответ API на подписку:");

        PrintRawJson(subscription);

        var channel =
            $"$alerts:donation_{user.Id}";

        var channelToken =
            ExtractChannelToken(
                subscription,
                channel);

        if (string.IsNullOrWhiteSpace(channelToken))
        {
            throw new InvalidOperationException(
                "Donation Alerts не вернул токен приватного канала.");
        }

        var subscribeMessage =
            JsonSerializer.Serialize(
                new
                {
                    @params = new
                    {
                        channel,
                        token = channelToken
                    },
                    method = 1,
                    id = 2
                });

        await SendWebSocketMessageAsync(
            socket,
            subscribeMessage,
            cancellationToken);

        var subscribeResponse =
            await ReceiveWebSocketMessageAsync(
                socket,
                cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "Ответ Centrifugo на подписку:");

        PrintRawJson(subscribeResponse);

        Console.WriteLine();
        Console.WriteLine(
            $"Подписка активна: {channel}");

        Console.WriteLine();
        Console.WriteLine(
            "Ожидаем новые донаты.");
        Console.WriteLine(
            "Сделай тестовый донат и НЕ закрывай это окно.");
        Console.WriteLine(
            "Для остановки нажми Ctrl+C.");

        while (
            socket.State == WebSocketState.Open &&
            !cancellationToken.IsCancellationRequested)
        {
            var message =
                await ReceiveWebSocketMessageAsync(
                    socket,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine(
                "========== DONATION ALERT EVENT ==========");

            PrintRawJson(message);

            Console.WriteLine();
            Console.WriteLine(
                "==========================================");
        }
    }

    private async Task<string> SubscribeAsync(
        string accessToken,
        string clientId,
        long userId,
        CancellationToken cancellationToken)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                $"{ApiBaseUrl}/centrifuge/subscribe",
                accessToken);

        var channel =
            $"$alerts:donation_{userId}";

        request.Content =
            new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        channels = new[] { channel },
                        client = clientId
                    }),
                Encoding.UTF8,
                "application/json");

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var content =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Centrifuge subscription failed: " +
                $"HTTP {(int)response.StatusCode} " +
                $"{response.StatusCode}. {content}");
        }

        return content;
    }

    private static string? ExtractClientId(string json)
    {
        using var document =
            JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty(
                "result",
                out var result) &&
            result.TryGetProperty(
                "client",
                out var client)
            ? client.GetString()
            : null;
    }

    private static string? ExtractChannelToken(
        string json,
        string channel)
    {
        using var document =
            JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(
                "channels",
                out var channels) ||
            channels.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in channels.EnumerateArray())
        {
            if (!item.TryGetProperty(
                    "channel",
                    out var channelProperty) ||
                !item.TryGetProperty(
                    "token",
                    out var tokenProperty))
            {
                continue;
            }

            if (string.Equals(
                channelProperty.GetString(),
                channel,
                StringComparison.Ordinal))
            {
                return tokenProperty.GetString();
            }
        }

        return null;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string url,
        string accessToken)
    {
        var request =
            new HttpRequestMessage(
                method,
                url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        return request;
    }

    private async Task<T> SendJsonAsync<T>(
        HttpRequestMessage request)
    {
        using var response =
            await _httpClient.SendAsync(request);

        var content =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Donation Alerts API returned " +
                $"HTTP {(int)response.StatusCode} " +
                $"{response.StatusCode}: {content}");
        }

        var result =
            JsonSerializer.Deserialize<T>(
                content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (result == null)
        {
            throw new InvalidOperationException(
                "Donation Alerts API returned an empty or invalid JSON response.");
        }

        return result;
    }

    private static async Task SendWebSocketMessageAsync(
        ClientWebSocket socket,
        string message,
        CancellationToken cancellationToken)
    {
        var bytes =
            Encoding.UTF8.GetBytes(message);

        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    private static async Task<string> ReceiveWebSocketMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var stream =
            new MemoryStream();

        var buffer =
            new byte[8192];

        while (true)
        {
            var result =
                await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

            if (result.MessageType ==
                WebSocketMessageType.Close)
            {
                throw new WebSocketException(
                    "Donation Alerts Centrifugo закрыла WebSocket соединение.");
            }

            if (result.MessageType !=
                WebSocketMessageType.Text)
            {
                continue;
            }

            await stream.WriteAsync(
                buffer.AsMemory(0, result.Count),
                cancellationToken);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(
            stream.ToArray());
    }

    private static void PrintRawJson(string json)
    {
        try
        {
            using var document =
                JsonDocument.Parse(json);

            Console.WriteLine(
                JsonSerializer.Serialize(
                    document.RootElement,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch
        {
            Console.WriteLine(json);
        }
    }

    public sealed class DonationAlertsDataResponse<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    public sealed class DonationAlertsUser
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("avatar")]
        public string Avatar { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("socket_connection_token")]
        public string SocketConnectionToken { get; set; } = string.Empty;
    }
}
