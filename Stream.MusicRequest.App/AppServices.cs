using Stream.MusicRequest.Twitch.Services;
using Stream.MusicRequest.Yandex.Services;

namespace Stream.MusicRequest.App;

public class AppServices
{
    public YandexAuthService YandexAuthService { get; }

    public TwitchAuthService TwitchAuthService { get; }

    public TwitchApiClient TwitchApiClient { get; }

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
    }
}