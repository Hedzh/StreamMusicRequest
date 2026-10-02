using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stream.MusicRequest.DonationAlerts.Models;
using Stream.MusicRequest.DonationAlerts.Storage;

namespace Stream.MusicRequest.DonationAlerts.Client;

public sealed class DonationAlertsClient
{
    private const string ApiBaseUrl = "https://www.donationalerts.com/api/v1";
    private const string OAuthAuthorizeUrl = "https://www.donationalerts.com/oauth/authorize";
    private const string CentrifugoUrl = "wss://centrifugo.donationalerts.com/connection/websocket";

    private readonly HttpClient _httpClient;
    private readonly DonationAlertsTokenStorage _tokenStorage;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _connectionCts;
    private readonly HashSet<long> _processedDonationIds = new();

    public DonationAlertsClient(HttpClient httpClient, DonationAlertsTokenStorage tokenStorage)
    {
        _httpClient = httpClient;
        _tokenStorage = tokenStorage;
    }

    public event EventHandler<DonationEvent>? DonationReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public DonationAlertsUser? CurrentUser { get; private set; }
    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task<DonationAlertsUser?> LoadSavedAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokenStorage.LoadAsync();
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var user = await GetCurrentUserAsync(token, cancellationToken);
            CurrentUser = user;
            return user;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            await _tokenStorage.ClearAsync();
            CurrentUser = null;
            return null;
        }
    }

    public async Task<DonationAlertsUser> AuthorizeAsync(
        string clientId,
        string redirectUri,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            clientId.Contains("PUT_YOUR_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Client ID Donation Alerts ещё не настроен.");

        var url = BuildAuthorizationUrl(clientId, redirectUri, scopes);
        var token = await WaitForImplicitAccessTokenAsync(url, redirectUri, cancellationToken);
        await _tokenStorage.SaveAsync(token);

        var user = await GetCurrentUserAsync(token, cancellationToken);
        CurrentUser = user;
        return user;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokenStorage.LoadAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Donation Alerts не авторизован.");

        var user = await GetCurrentUserAsync(token, cancellationToken);
        CurrentUser = user;
        await DisconnectAsync();

        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _connectionCts.Token;
        _socket = new ClientWebSocket { Options = { KeepAliveInterval = TimeSpan.FromSeconds(20) } };

        try
        {
            await _socket.ConnectAsync(new Uri(CentrifugoUrl), ct);

            var connect = JsonSerializer.Serialize(new
            {
                @params = new { token = user.SocketConnectionToken },
                id = 1
            });
            await SendAsync(connect, ct);

            var connectResponse = await ReceiveAsync(ct);
            var clientId = ExtractClientId(connectResponse)
                ?? throw new InvalidOperationException("Donation Alerts не вернул Centrifugo Client ID.");

            var channel = $"$alerts:donation_{user.Id}";
            var subscription = await SubscribeAsync(token, clientId, user.Id, ct);
            var channelToken = ExtractChannelToken(subscription, channel)
                ?? throw new InvalidOperationException("Donation Alerts не вернул токен приватного канала.");

            var subscribe = JsonSerializer.Serialize(new
            {
                @params = new { channel, token = channelToken },
                method = 1,
                id = 2
            });
            await SendAsync(subscribe, ct);
            await ReceiveAsync(ct);

            ConnectionStateChanged?.Invoke(this, true);

            while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReceiveAsync(ct);
                var donation = TryParseDonation(message);

                if (donation != null && _processedDonationIds.Add(donation.Id))
                    DonationReceived?.Invoke(this, donation);
            }
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    public async Task DisconnectAsync()
    {
        _connectionCts?.Cancel();

        if (_socket != null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", CancellationToken.None);
            }
            catch { }

            _socket.Dispose();
            _socket = null;
        }

        _connectionCts?.Dispose();
        _connectionCts = null;
        ConnectionStateChanged?.Invoke(this, false);
    }

    public async Task LogoutAsync()
    {
        await DisconnectAsync();
        await _tokenStorage.ClearAsync();
        CurrentUser = null;
    }

    private static string BuildAuthorizationUrl(string clientId, string redirectUri, IEnumerable<string> scopes)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "token",
            ["scope"] = string.Join(" ", scopes)
        };

        return OAuthAuthorizeUrl + "?" +
               string.Join("&", query.Select(x =>
                   $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private async Task<string> WaitForImplicitAccessTokenAsync(
        string authorizationUrl,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RedirectUri Donation Alerts должен быть локальным HTTP URI 127.0.0.1.");

        using var listener = new TcpListener(IPAddress.Loopback, uri.Port);
        listener.Start();

        Process.Start(new ProcessStartInfo
        {
            FileName = authorizationUrl,
            UseShellExecute = true
        });

        try
        {
            while (true)
            {
                using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = tcp.GetStream();

                var buffer = new byte[16 * 1024];
                var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
                var request = Encoding.UTF8.GetString(buffer, 0, count);
                var first = request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                if (string.IsNullOrWhiteSpace(first)) continue;
                var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                if (request.StartsWith("POST ", StringComparison.OrdinalIgnoreCase))
                {
                    var separator = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (separator < 0) continue;

                    var body = request[(separator + 4)..];
                    var token = GetFormParameter(body, "access_token");
                    var error = GetFormParameter(body, "error");
                    var description = GetFormParameter(body, "error_description");

                    await WriteResponseAsync(stream, "<html><body><h2>Готово</h2><p>Вернитесь в StreamMusicRequest.</p></body></html>", cancellationToken);

                    if (!string.IsNullOrWhiteSpace(error))
                        throw new InvalidOperationException($"Donation Alerts OAuth returned error '{error}': {description}");

                    if (!string.IsNullOrWhiteSpace(token)) return token;
                    continue;
                }

                if (!parts[1].StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
                    continue;

                const string html = """
<!doctype html>
<html><head><meta charset="utf-8"><title>Donation Alerts</title></head>
<body><h2>Donation Alerts authorization</h2>
<p>Передача результата авторизации...</p>
<script>
const p=new URLSearchParams(location.hash.substring(1));
const f=new URLSearchParams();
if(p.get('access_token'))f.set('access_token',p.get('access_token'));
if(p.get('error'))f.set('error',p.get('error'));
if(p.get('error_description'))f.set('error_description',p.get('error_description'));
fetch('/callback',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:f.toString()})
.then(()=>document.body.innerHTML='<h2>Готово</h2><p>Вернитесь в StreamMusicRequest.</p>');
</script></body></html>
""";
                await WriteResponseAsync(stream, html, cancellationToken);
            }
        }
        finally { listener.Stop(); }
    }

    private async Task<DonationAlertsUser> GetCurrentUserAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{ApiBaseUrl}/user/oauth", token);
        var response = await SendJsonAsync<DonationAlertsDataResponse<DonationAlertsUser>>(request, cancellationToken);
        return response.Data ?? throw new InvalidOperationException("Donation Alerts /user/oauth не вернул пользователя.");
    }

    private async Task<string> SubscribeAsync(string token, string clientId, long userId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"{ApiBaseUrl}/centrifuge/subscribe", token);
        var channel = $"$alerts:donation_{userId}";
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { channels = new[] { channel }, client = clientId }),
            Encoding.UTF8, "application/json");
        return await SendTextAsync(request, cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        var content = await SendTextAsync(request, ct);
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Donation Alerts вернул некорректный JSON.");
    }

    private async Task<string> SendTextAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Donation Alerts API returned HTTP {(int)response.StatusCode}: {content}");

        return content;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task SendAsync(string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await _socket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<string> ReceiveAsync(CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var result = await _socket!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Donation Alerts Centrifugo закрыла WebSocket.");

            if (result.MessageType != WebSocketMessageType.Text) continue;

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct);
            if (result.EndOfMessage) break;
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static DonationEvent? TryParseDonation(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            if (!document.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("data", out var envelope) ||
                !envelope.TryGetProperty("data", out var data))
                return null;

            return JsonSerializer.Deserialize<DonationEvent>(
                data.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return null; }
    }

    private static string? ExtractClientId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("result", out var result) &&
               result.TryGetProperty("client", out var client)
            ? client.GetString() : null;
    }

    private static string? ExtractChannelToken(string json, string channel)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("channels", out var channels) ||
            channels.ValueKind != JsonValueKind.Array) return null;

        foreach (var item in channels.EnumerateArray())
        {
            if (item.TryGetProperty("channel", out var ch) &&
                item.TryGetProperty("token", out var token) &&
                string.Equals(ch.GetString(), channel, StringComparison.Ordinal))
                return token.GetString();
        }
        return null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(bytes, ct);
    }

    private static string? GetFormParameter(string body, string name)
    {
        foreach (var pair in body.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            if (!key.Equals(name, StringComparison.Ordinal)) continue;

            return Uri.UnescapeDataString(parts[1].Replace("+", " "));
        }
        return null;
    }

    public sealed class DonationAlertsDataResponse<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    public sealed class DonationAlertsUser
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("avatar")] public string Avatar { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("socket_connection_token")] public string SocketConnectionToken { get; set; } = string.Empty;
    }
}
