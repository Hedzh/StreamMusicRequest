using Stream.MusicRequest.Infrastructure.Storage;

namespace Stream.MusicRequest.DonationAlerts.Storage;

public sealed class DonationAlertsTokenStorage
{
    private const string AccessTokenKey =
        "DonationAlerts.Implicit.AccessToken";

    private readonly DpapiProtectedStorage _storage;

    public DonationAlertsTokenStorage()
    {
        var directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "Auth");

        _storage =
            new DpapiProtectedStorage(directory);
    }

    public Task SaveAsync(string accessToken) =>
        _storage.SaveAsync(
            AccessTokenKey,
            accessToken);

    public Task<string?> LoadAsync() =>
        _storage.LoadAsync(
            AccessTokenKey);

    public Task ClearAsync() =>
        _storage.DeleteAsync(
            AccessTokenKey);
}
