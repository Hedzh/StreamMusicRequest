using Stream.MusicRequest.Core.Services;
using Stream.MusicRequest.DonationAlerts.Client;
using Stream.MusicRequest.DonationAlerts.Services;
using Stream.MusicRequest.DonationAlerts.Storage;
using Stream.MusicRequest.Infrastructure.Storage;
using Stream.MusicRequest.Yandex.Sources;
using Stream.MusicRequest.Twitch.EventSub;
using Stream.MusicRequest.Twitch.Models;
using Stream.MusicRequest.Twitch.Services;
using Stream.MusicRequest.Yandex.Services;

namespace Stream.MusicRequest.App;

public class AppServices
{
    private readonly TwitchRewardSettingsStorage
        _twitchRewardSettingsStorage;

    public YandexAuthService YandexAuthService { get; }

    public TwitchAuthService TwitchAuthService { get; }

    public TwitchApiClient TwitchApiClient { get; }

    public TwitchService TwitchService { get; }

    public DonationAlertsMusicService DonationAlertsMusicService { get; }

    public IReadOnlyList<TwitchRewardBinding>
        TwitchRewardBindings
    { get; }

    public AppServices(
        YandexAuthService yandexAuthService,
        TwitchAuthService twitchAuthService,
        TwitchApiClient twitchApiClient,
        TwitchRewardSettingsStorage twitchRewardSettingsStorage,
        MusicPlaybackController playbackController,
        MusicRequestService requestService,
        YandexMusicSource yandexMusicSource)
    {
        YandexAuthService =
            yandexAuthService
            ?? throw new ArgumentNullException(
                nameof(yandexAuthService));

        TwitchAuthService =
            twitchAuthService
            ?? throw new ArgumentNullException(
                nameof(twitchAuthService));

        TwitchApiClient =
            twitchApiClient
            ?? throw new ArgumentNullException(
                nameof(twitchApiClient));

        _twitchRewardSettingsStorage =
            twitchRewardSettingsStorage
            ?? throw new ArgumentNullException(
                nameof(twitchRewardSettingsStorage));

        TwitchRewardBindings =
            new List<TwitchRewardBinding>
            {
                TwitchRewardService
                    .CreateDefaultOrderMusicBinding(),

                TwitchRewardService
                    .CreateDefaultSkipTrackBinding()
            };

        var twitchEventSubClient =
            new TwitchEventSubClient();

        TwitchService =
            new TwitchService(
                TwitchAuthService,
                TwitchApiClient,
                twitchEventSubClient,
                TwitchRewardBindings);
        var donationAlertsHttpClient =
            new HttpClient();

        var donationAlertsTokenStorage =
            new DonationAlertsTokenStorage();

        var donationAlertsSettingsStorage =
            new DonationAlertsSettingsStorage();

        var donationAlertsClient =
            new DonationAlertsClient(
                donationAlertsHttpClient,
                donationAlertsTokenStorage);

        var donationAlertsSettings =
            donationAlertsSettingsStorage.Load();

        var donationAlertsClassifier =
            new DonationOrderClassifier(
                donationAlertsSettings.DonationThresholds);

        DonationAlertsMusicService =
            new DonationAlertsMusicService(
                donationAlertsClient,
                donationAlertsClassifier,
                donationAlertsSettingsStorage,
                requestService,
                playbackController,
                yandexMusicSource);

        DonationAlertsMusicService.LoadSettings();

    }

    public async Task LoadTwitchRewardSettingsAsync()
    {
        var settings =
            await _twitchRewardSettingsStorage
                .LoadAsync();

        var orderMusicBinding =
            TwitchRewardBindings
                .FirstOrDefault(
                    binding =>
                        binding.FeatureKey ==
                        TwitchRewardService.OrderMusicFeatureKey);

        if (orderMusicBinding != null)
        {
            orderMusicBinding.RewardId =
                settings.OrderMusicRewardId;

            orderMusicBinding.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    orderMusicBinding.RewardId);
        }

        var skipTrackBinding =
            TwitchRewardBindings
                .FirstOrDefault(
                    binding =>
                        binding.FeatureKey ==
                        TwitchRewardService.SkipTrackFeatureKey);

        if (skipTrackBinding != null)
        {
            skipTrackBinding.RewardId =
                settings.SkipTrackRewardId;

            skipTrackBinding.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    skipTrackBinding.RewardId);
        }
    }

    public async Task SaveTwitchRewardSettingAsync(
        string featureKey,
        string rewardId)
    {
        featureKey =
            featureKey?.Trim()
            ?? string.Empty;

        rewardId =
            rewardId?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(featureKey))
        {
            throw new ArgumentException(
                "Ключ функции Twitch-награды не указан.",
                nameof(featureKey));
        }

        if (featureKey != TwitchRewardService.OrderMusicFeatureKey &&
            featureKey != TwitchRewardService.SkipTrackFeatureKey)
        {
            throw new ArgumentException(
                "Неизвестная функция Twitch-награды.",
                nameof(featureKey));
        }

        var settings =
            await _twitchRewardSettingsStorage
                .LoadAsync();

        if (featureKey ==
            TwitchRewardService.OrderMusicFeatureKey)
        {
            settings.OrderMusicRewardId =
                rewardId;
        }
        else
        {
            settings.SkipTrackRewardId =
                rewardId;
        }

        var binding =
            TwitchRewardBindings
                .FirstOrDefault(
                    item =>
                        item.FeatureKey ==
                        featureKey);

        if (binding != null)
        {
            binding.RewardId =
                rewardId;

            binding.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    rewardId);
        }

        await _twitchRewardSettingsStorage
            .SaveAsync(settings);
    }

    public async Task SaveTwitchRewardSettingsAsync(
        string orderMusicRewardId,
        string skipTrackRewardId)
    {
        var normalizedOrderMusicRewardId =
            orderMusicRewardId?.Trim()
            ?? string.Empty;

        var normalizedSkipTrackRewardId =
            skipTrackRewardId?.Trim()
            ?? string.Empty;

        var orderMusicBinding =
            TwitchRewardBindings
                .FirstOrDefault(
                    binding =>
                        binding.FeatureKey ==
                        TwitchRewardService.OrderMusicFeatureKey);

        if (orderMusicBinding != null)
        {
            orderMusicBinding.RewardId =
                normalizedOrderMusicRewardId;

            orderMusicBinding.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    normalizedOrderMusicRewardId);
        }

        var skipTrackBinding =
            TwitchRewardBindings
                .FirstOrDefault(
                    binding =>
                        binding.FeatureKey ==
                        TwitchRewardService.SkipTrackFeatureKey);

        if (skipTrackBinding != null)
        {
            skipTrackBinding.RewardId =
                normalizedSkipTrackRewardId;

            skipTrackBinding.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    normalizedSkipTrackRewardId);
        }

        await _twitchRewardSettingsStorage.SaveAsync(
            new TwitchRewardSettings
            {
                OrderMusicRewardId =
                    normalizedOrderMusicRewardId,

                SkipTrackRewardId =
                    normalizedSkipTrackRewardId
            });
    }
}