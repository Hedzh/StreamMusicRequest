using Stream.MusicRequest.Twitch.EventSub;
using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Services;

public class TwitchService : ITwitchService
{
    private readonly TwitchAuthService _authService;

    private readonly TwitchApiClient _apiClient;

    private readonly TwitchEventSubClient _eventSubClient;

    private readonly IReadOnlyList<TwitchRewardBinding>
        _rewardBindings;

    private TwitchUser? _currentUser;

    private CancellationTokenSource?
        _connectionCancellation;

    private bool _isConnected;

    private bool _connectionInProgress;

    public event EventHandler<
        TwitchRewardActionReceivedEventArgs>?
        RewardActionReceived;

    public TwitchService(
        TwitchAuthService authService,
        TwitchApiClient apiClient,
        TwitchEventSubClient eventSubClient,
        IEnumerable<TwitchRewardBinding> rewardBindings)
    {
        _authService =
            authService
            ?? throw new ArgumentNullException(
                nameof(authService));

        _apiClient =
            apiClient
            ?? throw new ArgumentNullException(
                nameof(apiClient));

        _eventSubClient =
            eventSubClient
            ?? throw new ArgumentNullException(
                nameof(eventSubClient));

        _rewardBindings =
            rewardBindings?.ToList()
            ?? throw new ArgumentNullException(
                nameof(rewardBindings));

        _eventSubClient.Connected +=
            EventSubClient_Connected;

        _eventSubClient.RewardRedemptionReceived +=
            EventSubClient_RewardRedemptionReceived;
    }

    public async Task ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isConnected ||
            _connectionInProgress)
        {
            return;
        }

        _connectionInProgress =
            true;

        try
        {
            var accessToken =
                await _authService.GetValidAccessTokenAsync(
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return;
            }

            _currentUser =
                await _apiClient.GetCurrentUserAsync(
                    accessToken,
                    cancellationToken);

            await ValidateRewardsAsync(
                accessToken,
                _currentUser.Id,
                cancellationToken);

            _connectionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            await _eventSubClient.ConnectAsync(
                _connectionCancellation.Token);
        }
        finally
        {
            _connectionInProgress =
                false;
        }
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

    private async Task ValidateRewardsAsync(
        string accessToken,
        string broadcasterId,
        CancellationToken cancellationToken)
    {
        var rewardService =
            new TwitchRewardService(
                _apiClient);

        foreach (var binding in _rewardBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    binding.RewardId))
            {
                binding.IsEnabled =
                    false;

                continue;
            }

            try
            {
                await rewardService.ValidateRewardAsync(
                    accessToken,
                    broadcasterId,
                    binding,
                    cancellationToken);
            }
            catch
            {
                binding.IsEnabled =
                    false;
            }
        }
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

            var accessToken =
                await _authService.GetValidAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return;
            }

            await _apiClient.CreateEventSubSubscriptionAsync(
                accessToken,
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

        var binding =
            FindBinding(
                redemption.Reward.Id);

        if (binding == null)
        {
            return;
        }

        if (!binding.IsEnabled)
        {
            return;
        }

        var action =
            TwitchRewardService.ResolveAction(
                binding);

        if (action ==
            TwitchRewardAction.Unknown)
        {
            return;
        }

        RewardActionReceived?.Invoke(
            this,
            new TwitchRewardActionReceivedEventArgs(
                action,
                redemption));
    }

    private TwitchRewardBinding? FindBinding(
        string rewardId)
    {
        if (string.IsNullOrWhiteSpace(
                rewardId))
        {
            return null;
        }

        foreach (var binding in _rewardBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (!binding.RewardId.Equals(
                    rewardId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            return binding;
        }

        return null;
    }
}