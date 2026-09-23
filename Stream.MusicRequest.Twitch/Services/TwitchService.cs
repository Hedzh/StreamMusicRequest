using MusicRequestModel =
    Stream.MusicRequest.Core.Requests.MusicRequest;

using Stream.MusicRequest.Twitch.EventSub;
using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Services;

public class TwitchService : ITwitchService
{
    private readonly TwitchAuthService _authService;
    private readonly TwitchApiClient _apiClient;
    private readonly TwitchEventSubClient _eventSubClient;

    private TwitchUser? _currentUser;

    private CancellationTokenSource? _connectionCancellation;

    private bool _isConnected;

    public event EventHandler<MusicRequestModel>? MusicRequestReceived;

    public TwitchService(
        TwitchAuthService authService,
        TwitchApiClient apiClient,
        TwitchEventSubClient eventSubClient)
    {
        _authService =
            authService;

        _apiClient =
            apiClient;

        _eventSubClient =
            eventSubClient;

        _eventSubClient.Connected +=
            EventSubClient_Connected;

        _eventSubClient.RewardRedemptionReceived +=
            EventSubClient_RewardRedemptionReceived;
    }

    public async Task ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return;
        }

        var token =
            await _authService.TryRestoreAsync();

        if (token == null)
        {
            throw new InvalidOperationException(
                "Twitch не авторизован.");
        }

        _currentUser =
            await _apiClient.GetCurrentUserAsync(
                token.AccessToken,
                cancellationToken);

        _connectionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        await _eventSubClient.ConnectAsync(
            _connectionCancellation.Token);
    }

    public async Task DisconnectAsync()
    {
        _connectionCancellation?.Cancel();

        _connectionCancellation?.Dispose();

        _connectionCancellation =
            null;

        await _eventSubClient.DisconnectAsync();

        _currentUser =
            null;

        _isConnected =
            false;
    }

    private void EventSubClient_Connected(
        object? sender,
        TwitchEventSubWelcomeMessage message)
    {
        _isConnected =
            true;

        _ = RegisterSubscriptionsAsync(
            message);
    }

    private async Task RegisterSubscriptionsAsync(
        TwitchEventSubWelcomeMessage message)
    {
        try
        {
            if (_currentUser == null)
            {
                return;
            }

            var token =
                await _authService.TryRestoreAsync();

            if (token == null)
            {
                return;
            }

            await _apiClient.CreateEventSubSubscriptionAsync(
                token.AccessToken,
                message.Payload.Session.Id,
                _currentUser.Id);
        }
        catch
        {
            _isConnected =
                false;

            await _eventSubClient.DisconnectAsync();
        }
    }

    private void EventSubClient_RewardRedemptionReceived(
        object? sender,
        TwitchRewardRedemption redemption)
    {
        if (_currentUser == null)
        {
            return;
        }

        if (!redemption.BroadcasterUserId.Equals(
                _currentUser.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        var request =
            new MusicRequestModel
            {
                Id =
                    redemption.Id,

                ViewerName =
                    redemption.UserName,

                SourceUrl =
                    redemption.UserInput,

                CreatedAt =
                    redemption.RedeemedAt
            };

        MusicRequestReceived?.Invoke(
            this,
            request);
    }
}