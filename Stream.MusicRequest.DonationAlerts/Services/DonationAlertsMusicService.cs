using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Queue;
using Stream.MusicRequest.Core.Requests;
using Stream.MusicRequest.Core.Services;
using Stream.MusicRequest.DonationAlerts.Client;
using Stream.MusicRequest.DonationAlerts.Configuration;
using Stream.MusicRequest.DonationAlerts.Models;
using Stream.MusicRequest.DonationAlerts.Storage;
using Stream.MusicRequest.Yandex.Sources;

namespace Stream.MusicRequest.DonationAlerts.Services;

public sealed class DonationAlertsMusicService
{
    private readonly DonationAlertsClient _client;
    private readonly DonationOrderClassifier _classifier;
    private readonly DonationAlertsSettingsStorage _settingsStorage;
    private readonly MusicRequestService _requestService;
    private readonly MusicPlaybackController _playbackController;
    private readonly YandexMusicSource _yandexSource;

    public DonationAlertsMusicService(
        DonationAlertsClient client,
        DonationOrderClassifier classifier,
        DonationAlertsSettingsStorage settingsStorage,
        MusicRequestService requestService,
        MusicPlaybackController playbackController,
        YandexMusicSource yandexSource)
    {
        _client = client;
        _classifier = classifier;
        _settingsStorage = settingsStorage;
        _requestService = requestService;
        _playbackController = playbackController;
        _yandexSource = yandexSource;

        _client.DonationReceived += Client_DonationReceived;
        _client.ConnectionStateChanged += Client_ConnectionStateChanged;
    }

    public DonationAlertsSettings Settings { get; private set; } = new();

    public bool IsConnected => _client.IsConnected;

    public DonationAlertsClient.DonationAlertsUser? CurrentUser =>
        _client.CurrentUser;

    public event EventHandler<bool>? ConnectionStateChanged;

    public void LoadSettings()
    {
        Settings = _settingsStorage.Load();
    }

    public Task LoadSettingsAsync()
    {
        LoadSettings();
        return Task.CompletedTask;
    }

    public async Task<bool> RestoreAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        var user =
            await _client.LoadSavedAuthorizationAsync(
                cancellationToken);

        if (user == null)
            return false;

        _ = ConnectInBackgroundAsync(cancellationToken);
        return true;
    }

    public async Task AuthorizeAndConnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _client.AuthorizeAsync(
            DonationAlertsConfiguration.ClientId,
            DonationAlertsConfiguration.RedirectUri,
            DonationAlertsConfiguration.Scopes,
            cancellationToken);

        await _client.ConnectAsync(
            cancellationToken);
    }

    public Task DisconnectAsync() =>
        _client.DisconnectAsync();

    public Task LogoutAsync() =>
        _client.LogoutAsync();

    public async Task SaveSettingsAsync(
        DonationAlertsSettings settings)
    {
        Settings = settings;
        _classifier.UpdateThresholds(
            settings.DonationThresholds);
        await _settingsStorage.SaveAsync(settings);
    }

    private async Task ConnectInBackgroundAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Donation Alerts: ошибка подключения.",
                ex);
        }
    }

    private async void Client_DonationReceived(
        object? sender,
        DonationEvent donation)
    {
        try
        {
            if (!Settings.MusicOrdersEnabled)
                return;

            var orderType =
                _classifier.Classify(donation);

            if (orderType == DonationOrderType.Ignore)
                return;

            var message =
                donation.Message?.Trim();

            if (string.IsNullOrWhiteSpace(message) ||
                !_yandexSource.CanHandleUrl(message))
            {
                FileLogger.Info(
                    $"Donation Alerts: донат #{donation.Id} " +
                    "не содержит поддерживаемую ссылку на трек.");

                return;
            }

            var request =
                new MusicRequest
                {
                    Id = $"donation-{donation.Id}",
                    ViewerName = donation.Username,
                    SourceUrl = message,
                    CreatedAt =
                        DateTime.TryParse(
                            donation.CreatedAt,
                            out var createdAt)
                            ? createdAt
                            : DateTime.UtcNow
                };

            var result =
                await _requestService.AddRequestAsync(
                    request);

            if (!result.Success ||
                result.Item == null)
            {
                FileLogger.Info(
                    $"Donation Alerts: донат #{donation.Id} " +
                    "не удалось преобразовать в музыкальный заказ.");

                return;
            }

            if (orderType == DonationOrderType.Normal)
            {
                await _playbackController
                    .AddDonationRequestAsync(
                        result.Item);

                FileLogger.Info(
                    $"Donation Alerts: донат #{donation.Id} " +
                    "добавлен как обычный заказ.");

                return;
            }

            await _playbackController
                .PlaySuperPriorityDonationAsync(
                    result.Item);

            FileLogger.Info(
                $"Donation Alerts: донат #{donation.Id} " +
                "запущен как SUPER-PRIORITY.");
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Donation Alerts: ошибка обработки доната.",
                ex);
        }
    }

    private void Client_ConnectionStateChanged(
        object? sender,
        bool isConnected)
    {
        ConnectionStateChanged?.Invoke(
            this,
            isConnected);
    }
}
