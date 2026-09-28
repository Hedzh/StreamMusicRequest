using Stream.MusicRequest.Infrastructure.Storage;

namespace Stream.MusicRequest.DonationAlerts.Test.Storage;

public sealed class DonationAlertsTestTokenStorage
{
    private const string AccessTokenKey = "DonationAlerts.Test.AccessToken";
    private const string RefreshTokenKey = "DonationAlerts.Test.RefreshToken";

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

    public Task SaveAsync(
        string accessToken,
        string refreshToken)
    {
        return SaveInternalAsync(
            accessToken,
            refreshToken);
    }

    public async Task<(string? AccessToken, string? RefreshToken)> LoadAsync()
    {
        var accessToken =
            await _storage.LoadAsync(
                AccessTokenKey);

        var refreshToken =
            await _storage.LoadAsync(
                RefreshTokenKey);

        return (
            accessToken,
            refreshToken);
    }

    public async Task ClearAsync()
    {
        await _storage.DeleteAsync(
            AccessTokenKey);

        await _storage.DeleteAsync(
            RefreshTokenKey);
    }

    private async Task SaveInternalAsync(
        string accessToken,
        string refreshToken)
    {
        await _storage.SaveAsync(
            AccessTokenKey,
            accessToken);

        await _storage.SaveAsync(
            RefreshTokenKey,
            refreshToken);
    }
}
