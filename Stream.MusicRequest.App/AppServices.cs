using Stream.MusicRequest.Twitch.EventSub;
using Stream.MusicRequest.Twitch.Models;
using Stream.MusicRequest.Twitch.Services;
using Stream.MusicRequest.Yandex.Services;

namespace Stream.MusicRequest.App;

public class AppServices
{
    public YandexAuthService YandexAuthService { get; }

    public TwitchAuthService TwitchAuthService { get; }

    public TwitchApiClient TwitchApiClient { get; }

    public TwitchService TwitchService { get; }

    public AppServices(
        YandexAuthService yandexAuthService,
        TwitchAuthService twitchAuthService,
        TwitchApiClient twitchApiClient)
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

        var twitchEventSubClient =
            new TwitchEventSubClient();

        var rewardBindings =
            new List<TwitchRewardBinding>
            {
                TwitchRewardService
                    .CreateDefaultOrderMusicBinding(),

                TwitchRewardService
                    .CreateDefaultSkipTrackBinding()
            };

        TwitchService =
            new TwitchService(
                TwitchAuthService,
                TwitchApiClient,
                twitchEventSubClient,
                rewardBindings);
    }
}