using YandexMusic;
using Stream.MusicRequest.Yandex.Storage;

namespace Stream.MusicRequest.Yandex.Services;

public class YandexAuthService
{
    private readonly YandexMusicClient _client;
    private readonly YandexAuthStorage _storage;

    public YandexAuthService(
        YandexMusicClient client,
        YandexAuthStorage storage)
    {
        _client = client;
        _storage = storage;
    }

    public bool IsAuthenticated =>
        _client.Authentication.IsAuthenticated;

    public async Task<bool> TryRestoreAsync()
    {
        var snapshot =
            await _storage.LoadAsync();

        if (snapshot == null)
        {
            return false;
        }

        _client.Authentication.Session.Import(
            snapshot);

        return _client.Authentication.IsAuthenticated;
    }

    public async Task AuthorizeAsync(
        Action<string, string>? onDeviceCode = null)
    {
        await _client.Authentication.SignInWithDeviceFlowAsync(
            deviceCode =>
            {
                onDeviceCode?.Invoke(
                    deviceCode.VerificationUrl,
                    deviceCode.UserCode);
            });

        var snapshot =
            _client.Authentication.Session.Export();

        await _storage.SaveAsync(
            snapshot);
    }

    public async Task SignOutAsync()
    {
        _client.Authentication.SignOut();

        await _storage.DeleteAsync();
    }
}