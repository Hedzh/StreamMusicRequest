using Stream.MusicRequest.Yandex.Services;

namespace Stream.MusicRequest.App;

public class AppServices
{
    public YandexAuthService YandexAuthService { get; }

    public AppServices(
        YandexAuthService yandexAuthService)
    {
        YandexAuthService =
            yandexAuthService
            ?? throw new ArgumentNullException(
                nameof(yandexAuthService));
    }
}