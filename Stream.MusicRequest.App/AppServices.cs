using Stream.MusicRequest.Infrastructure.Storage;
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

    public IReadOnlyList<TwitchRewardBinding>
        TwitchRewardBindings
    { get; }

    public AppServices(
        YandexAuthService yandexAuthService,
        TwitchAuthService twitchAuthService,
        TwitchApiClient twitchApiClient,
        TwitchRewardSettingsStorage twitchRewardSettingsStorage)
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