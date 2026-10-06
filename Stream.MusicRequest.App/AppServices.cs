using System.Net.Http;
using Stream.MusicRequest.Core.Services;
using Stream.MusicRequest.DonationAlerts.Client;
using Stream.MusicRequest.DonationAlerts.Services;
using Stream.MusicRequest.DonationAlerts.Storage;
using Stream.MusicRequest.Infrastructure.Storage;
using Stream.MusicRequest.Yandex.Sources;
using Stream.MusicRequest.Twitch.EventSub;
using Stream.MusicRequest.Twitch.Models;
using Stream.MusicRequest.Twitch.Configuration;
using Stream.MusicRequest.Twitch.Services;
using Stream.MusicRequest.Yandex.Services;

namespace Stream.MusicRequest.App;

public class AppServices
{
    private readonly TwitchRewardSettingsStorage
        _twitchRewardSettingsStorage;

    private readonly TwitchChatSettingsStorage
        _twitchChatSettingsStorage;

    public YandexAuthService YandexAuthService { get; }

    public TwitchAuthService TwitchAuthService { get; }

    public TwitchApiClient TwitchApiClient { get; }

    public TwitchService TwitchService { get; }

    public DonationAlertsMusicService DonationAlertsMusicService { get; }

    public IBanService BanService { get; }

    public IReadOnlyList<TwitchRewardBinding>
        TwitchRewardBindings
    { get; }

    public TwitchChatSettings TwitchChatSettings { get; private set; } = new();

    public AppServices(
        YandexAuthService yandexAuthService,
        TwitchAuthService twitchAuthService,
        TwitchApiClient twitchApiClient,
        TwitchRewardSettingsStorage twitchRewardSettingsStorage,
        MusicPlaybackController playbackController,
        MusicRequestService requestService,
        YandexMusicSource yandexMusicSource,
        IBanService banService)
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

        BanService =
            banService
            ?? throw new ArgumentNullException(
                nameof(banService));

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

        _twitchChatSettingsStorage =
            new TwitchChatSettingsStorage(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StreamMusicRequest",
                    "Settings"));

        TwitchChatSettings = new TwitchChatSettings();

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

    public async Task LoadTwitchChatSettingsAsync()
    {
        TwitchChatSettings =
            await _twitchChatSettingsStorage.LoadAsync();
    }

    public async Task SaveTwitchChatSettingsAsync(
        bool successMessagesEnabled,
        bool errorMessagesEnabled)
    {
        TwitchChatSettings.SuccessMessagesEnabled =
            successMessagesEnabled;
        TwitchChatSettings.ErrorMessagesEnabled =
            errorMessagesEnabled;

        await _twitchChatSettingsStorage.SaveAsync(
            TwitchChatSettings);
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

    public async Task<IReadOnlyList<TwitchCustomReward>>
        GetTwitchCustomRewardsAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetTwitchAccessTokenAsync(
                cancellationToken);

        var user =
            await TwitchApiClient.GetCurrentUserAsync(
                accessToken,
                cancellationToken);

        return await TwitchApiClient.GetCustomRewardsAsync(
            accessToken,
            user.Id,
            cancellationToken);
    }

    public async Task<TwitchRewardValidationResult>
        ValidateTwitchRewardAsync(
            string featureKey,
            string rewardId,
            CancellationToken cancellationToken = default)
    {
        featureKey =
            featureKey?.Trim()
            ?? string.Empty;

        rewardId =
            rewardId?.Trim()
            ?? string.Empty;

        if (featureKey != TwitchRewardService.OrderMusicFeatureKey &&
            featureKey != TwitchRewardService.SkipTrackFeatureKey)
        {
            throw new ArgumentException(
                "Неизвестная функция Twitch-награды.",
                nameof(featureKey));
        }

        if (string.IsNullOrWhiteSpace(rewardId))
        {
            throw new ArgumentException(
                "Reward ID не указан.",
                nameof(rewardId));
        }

        if (!Guid.TryParse(rewardId, out _))
        {
            throw new ArgumentException(
                "Некорректный Reward ID. Используйте ID в формате UUID.",
                nameof(rewardId));
        }

        var accessToken =
            await GetTwitchAccessTokenAsync(
                cancellationToken);

        var user =
            await TwitchApiClient.GetCurrentUserAsync(
                accessToken,
                cancellationToken);

        var reward =
            await TwitchApiClient.GetCustomRewardAsync(
                accessToken,
                user.Id,
                rewardId,
                cancellationToken);

        if (reward == null)
        {
            throw new InvalidOperationException(
                "Награда с таким Reward ID не найдена.");
        }

        var settings =
            await _twitchRewardSettingsStorage
                .LoadAsync();

        var otherRewardId =
            featureKey ==
                TwitchRewardService.OrderMusicFeatureKey
                ? settings.SkipTrackRewardId
                : settings.OrderMusicRewardId;

        if (!string.IsNullOrWhiteSpace(otherRewardId) &&
            otherRewardId.Equals(
                reward.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            var otherFeature =
                featureKey ==
                    TwitchRewardService.OrderMusicFeatureKey
                    ? "«Пропустить трек»"
                    : "«Заказ музыки»";

            return new TwitchRewardValidationResult(
                reward,
                false,
                $"Эта Twitch-награда уже используется для функции {otherFeature}.");
        }

        var warnings =
            new List<string>();

        if (featureKey ==
                TwitchRewardService.OrderMusicFeatureKey &&
            !reward.IsUserInputRequired)
        {
            warnings.Add(
                "для «Заказ музыки» необходимо включить ввод текста пользователя");
        }

        if (featureKey ==
                TwitchRewardService.SkipTrackFeatureKey &&
            reward.IsUserInputRequired)
        {
            warnings.Add(
                "для «Пропустить трек» ввод текста пользователя должен быть отключён");
        }

        if (!reward.IsEnabled)
        {
            warnings.Add(
                "награда отключена в Twitch");
        }

        return new TwitchRewardValidationResult(
            reward,
            warnings.Count == 0,
            warnings.Count == 0
                ? null
                : "Награда несовместима с выбранной функцией: " +
                  string.Join("; ", warnings) +
                  ".");
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

    private async Task<string> GetTwitchAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var token =
            await TwitchAuthService.TryRestoreAsync();

        if (token == null ||
            string.IsNullOrWhiteSpace(
                token.AccessToken))
        {
            throw new InvalidOperationException(
                "Twitch не авторизован.");
        }

        return token.AccessToken;
    }
}
