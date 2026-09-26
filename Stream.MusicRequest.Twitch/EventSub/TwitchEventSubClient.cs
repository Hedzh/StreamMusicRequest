using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.EventSub;

public class TwitchEventSubClient
{
    private const string EventSubUrl =
        "wss://eventsub.wss.twitch.tv/ws";

    private ClientWebSocket? _webSocket;

    private Task? _receiveTask;

    private TaskCompletionSource<string>? _sessionReady;

    private string? _reconnectUrl;
    private bool _intentionalDisconnect;

    public bool IsConnected =>
        _webSocket?.State == WebSocketState.Open;

    public event EventHandler<TwitchEventSubWelcomeMessage>? Connected;

    public event EventHandler? Disconnected;

    public event EventHandler<TwitchRewardRedemption>?
        RewardRedemptionReceived;

    public async Task<string> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        if (_webSocket != null &&
            _webSocket.State == WebSocketState.Open)
        {
            if (_sessionReady != null)
            {
                return await _sessionReady.Task;
            }

            throw new InvalidOperationException(
                "EventSub уже подключён, но Session ID ещё не готов.");
        }

        var endpoint =
            string.IsNullOrWhiteSpace(_reconnectUrl)
                ? EventSubUrl
                : _reconnectUrl;

        _reconnectUrl = null;
        _intentionalDisconnect = false;

        _webSocket =
            new ClientWebSocket();

        _sessionReady =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        await _webSocket.ConnectAsync(
            new Uri(endpoint),
            cancellationToken);

        _receiveTask =
            ReceiveLoopAsync(
                _webSocket,
                cancellationToken);

        try
        {
            return await _sessionReady.Task.WaitAsync(
                cancellationToken);
        }
        catch
        {
            _sessionReady.TrySetCanceled();

            await DisconnectAsync();

            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;

        var webSocket =
            _webSocket;

        _webSocket = null;

        _sessionReady?.TrySetCanceled();

        _sessionReady = null;

        if (webSocket == null)
        {
            return;
        }

        try
        {
            if (webSocket.State ==
                WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Disconnect",
                    CancellationToken.None);
            }
        }
        catch
        {
            // Соединение могло быть уже закрыто Twitch.
        }
        finally
        {
            webSocket.Dispose();
        }

        _receiveTask = null;
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket webSocket,
        CancellationToken cancellationToken)
    {
        var buffer =
            new byte[16 * 1024];

        try
        {
            while (
                !cancellationToken.IsCancellationRequested &&
                webSocket.State == WebSocketState.Open)
            {
                using var message =
                    new MemoryStream();

                WebSocketReceiveResult result;

                do
                {
                    result =
                        await webSocket.ReceiveAsync(
                            buffer,
                            cancellationToken);

                    if (result.MessageType ==
                        WebSocketMessageType.Close)
                    {
                        _sessionReady?.TrySetException(
                            new InvalidOperationException(
                                "Twitch EventSub закрыл WebSocket до готовности сессии."));

                        return;
                    }

                    if (result.Count > 0)
                    {
                        message.Write(
                            buffer,
                            0,
                            result.Count);
                    }
                }
                while (!result.EndOfMessage);

                var json =
                    Encoding.UTF8.GetString(
                        message.ToArray());

                ProcessMessage(
                    json);
            }
        }
        catch (OperationCanceledException)
        {
            _sessionReady?.TrySetCanceled();
        }
        catch (Exception ex)
        {
            _sessionReady?.TrySetException(
                ex);
        }
        finally
        {
            if (!_intentionalDisconnect)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void ProcessMessage(
        string json)
    {
        try
        {
            using var document =
                JsonDocument.Parse(json);

            var root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "metadata",
                    out var metadata))
            {
                return;
            }

            if (!metadata.TryGetProperty(
                    "message_type",
                    out var messageTypeElement))
            {
                return;
            }

            var messageType =
                messageTypeElement.GetString();

            if (messageType ==
                "session_welcome")
            {
                ProcessWelcome(
                    root);

                return;
            }

            if (messageType ==
                "notification")
            {
                ProcessNotification(
                    root);

                return;
            }

            if (messageType ==
                "session_keepalive")
            {
                return;
            }

            if (messageType ==
                "session_reconnect")
            {
                ProcessReconnect(
                    root);

                return;
            }
        }
        catch
        {
            // Ошибка обработки отдельного
            // сообщения EventSub не должна
            // завершать обработку остальных сообщений.
        }
    }

    private void ProcessWelcome(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "payload",
                out var payload))
        {
            return;
        }

        if (!payload.TryGetProperty(
                "session",
                out var session))
        {
            return;
        }

        var message =
            JsonSerializer.Deserialize<TwitchEventSubWelcomeMessage>(
                root.GetRawText());

        if (message == null)
        {
            return;
        }

        var sessionId =
            session.GetProperty(
                "id")
            .GetString();

        if (string.IsNullOrWhiteSpace(
                sessionId))
        {
            return;
        }

        _sessionReady?.TrySetResult(
            sessionId);

        Connected?.Invoke(
            this,
            message);
    }

    private void ProcessNotification(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "payload",
                out var payload))
        {
            return;
        }

        if (!payload.TryGetProperty(
                "subscription",
                out var subscription))
        {
            return;
        }

        if (!subscription.TryGetProperty(
                "type",
                out var typeElement))
        {
            return;
        }

        var type =
            typeElement.GetString();

        if (type !=
            "channel.channel_points_custom_reward_redemption.add")
        {
            return;
        }

        if (!payload.TryGetProperty(
                "event",
                out var eventElement))
        {
            return;
        }

        var redemption =
            JsonSerializer.Deserialize<TwitchRewardRedemption>(
                eventElement.GetRawText());

        if (redemption == null)
        {
            return;
        }

        RewardRedemptionReceived?.Invoke(
            this,
            redemption);
    }

    private void ProcessReconnect(
        JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("session", out var session) ||
            !session.TryGetProperty("reconnect_url", out var reconnectUrlElement))
        {
            return;
        }

        var reconnectUrl = reconnectUrlElement.GetString();

        if (string.IsNullOrWhiteSpace(reconnectUrl))
        {
            return;
        }

        _reconnectUrl = reconnectUrl;
        _ = CloseForReconnectAsync();
    }

    private async Task CloseForReconnectAsync()
    {
        _intentionalDisconnect = true;

        var webSocket = _webSocket;

        if (webSocket == null)
        {
            _intentionalDisconnect = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Reconnect",
                    CancellationToken.None);
            }
        }
        catch
        {
        }
        finally
        {
            if (ReferenceEquals(_webSocket, webSocket))
            {
                _webSocket = null;
            }

            webSocket.Dispose();

            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}