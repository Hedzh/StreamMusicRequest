using Stream.MusicRequest.Infrastructure.Storage;

namespace Stream.MusicRequest.DonationAlerts.Test.Storage;

public sealed class DonationAlertsTestTokenStorage
{
    private const string AccessTokenKey =
        "DonationAlerts.Test.Implicit.AccessToken";

    private readonly DpapiProtectedStorage _storage;

    public DonationAlertsTestTokenStorage()
    {
        var directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "DonationAlertsTest");

        _storage =
            new DpapiProtectedStorage(directory);
    }

    public Task SaveAsync(string accessToken)
    {
        return _storage.SaveAsync(
            AccessTokenKey,
            accessToken);
    }

    public Task<string?> LoadAsync()
    {
        return _storage.LoadAsync(
            AccessTokenKey);
    }

    public Task ClearAsync()
    {
        return _storage.DeleteAsync(
            AccessTokenKey);
    }
}
