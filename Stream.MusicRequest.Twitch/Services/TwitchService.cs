using Stream.MusicRequest.Core.Diagnostics;
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

    private TaskCompletionSource<bool>?
        _subscriptionReady;

    private bool _isConnected;

    private bool _connectionInProgress;

    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public event EventHandler<bool>? ConnectionStateChanged;

    public event EventHandler? AuthenticationRequired;

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

        _eventSubClient.Disconnected +=
            EventSubClient_Disconnected;
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

            _subscriptionReady =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            await _eventSubClient.ConnectAsync(
                _connectionCancellation.Token);

            await _subscriptionReady.Task.WaitAsync(
                cancellationToken);

            StartConnectionMonitor();
        }
        finally
        {
            _connectionInProgress =
                false;
        }
    }

    public async Task DisconnectAsync()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = null;
        _monitorTask = null;

        _connectionCancellation?.Cancel();

        _connectionCancellation?.Dispose();

        _connectionCancellation =
            null;

        _subscriptionReady?.TrySetCanceled();

        _subscriptionReady =
            null;

        await _eventSubClient.DisconnectAsync();

        _currentUser =
            null;

        SetConnectionState(false);
    }

    private void StartConnectionMonitor()
    {
        if (_monitorTask != null && !_monitorTask.IsCompleted) return;
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = new CancellationTokenSource();
        _monitorTask = MonitorConnectionAsync(_monitorCancellation.Token);
    }

    private async Task MonitorConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(60), cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                await RecoverConnectionAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void EventSubClient_Disconnected(object? sender, EventArgs e)
    {
        SetConnectionState(false);

        FileLogger.Info("Twitch EventSub: соединение потеряно, запускается восстановление.");

        _ = RecoverConnectionAsync(CancellationToken.None);
    }

    private async Task RecoverConnectionAsync(CancellationToken cancellationToken)
    {
        await _reconnectLock.WaitAsync(cancellationToken);

        try
        {
            if (_isConnected || _connectionInProgress)
            {
                return;
            }

            var attempt = 0;

            while (!cancellationToken.IsCancellationRequested &&
                   !_isConnected)
            {
                attempt++;

                try
                {
                    using var attemptCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);

                    attemptCancellation.CancelAfter(
                        TimeSpan.FromSeconds(20));

                    var accessToken =
                        await _authService.GetValidAccessTokenAsync(
                            attemptCancellation.Token);

                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        FileLogger.Error(
                            "Twitch EventSub: OAuth-сессия больше не действительна. " +
                            "Требуется повторная авторизация.");

                        AuthenticationRequired?.Invoke(
                            this,
                            EventArgs.Empty);

                        return;
                    }

                    await ConnectAsync(
                        attemptCancellation.Token);

                    if (_isConnected)
                    {
                        FileLogger.Info(
                            $"Twitch EventSub: соединение успешно восстановлено с попытки {attempt}.");

                        return;
                    }
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    FileLogger.Error(
                        $"Twitch EventSub: попытка восстановления {attempt} прервана таймаутом.");
                }
                catch (Exception ex)
                {
                    FileLogger.Error(
                        $"Twitch EventSub: попытка восстановления {attempt} завершилась ошибкой.",
                        ex);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемая отмена при завершении приложения.
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private void SetConnectionState(bool isConnected)
    {
        if (_isConnected == isConnected)
        {
            return;
        }

        _isConnected = isConnected;

        ConnectionStateChanged?.Invoke(
            this,
            isConnected);
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

    private async void EventSubClient_Connected(
        object? sender,
        TwitchEventSubWelcomeMessage message)
    {
        try
        {
            await RegisterSubscriptionsAsync(
                message);

            SetConnectionState(true);

            _subscriptionReady?.TrySetResult(
                true);
        }
        catch (Exception ex)
        {
            SetConnectionState(false);

            _subscriptionReady?.TrySetException(
                ex);

            try
            {
                await _eventSubClient.DisconnectAsync();
            }
            catch
            {
                // EventSub уже мог быть отключён.
            }
        }
    }

    private async Task RegisterSubscriptionsAsync(
        TwitchEventSubWelcomeMessage message)
    {
        if (_currentUser == null)
        {
            throw new InvalidOperationException(
                "Невозможно зарегистрировать EventSub: " +
                "текущий Twitch-пользователь не определён.");
        }

        var accessToken =
            await _authService.GetValidAccessTokenAsync();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Невозможно зарегистрировать EventSub: " +
                "действующий Twitch access token отсутствует.");
        }

        await _apiClient.CreateEventSubSubscriptionAsync(
            accessToken,
            message.Payload.Session.Id,
            _currentUser.Id);
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