namespace Stream.MusicRequest.DonationAlerts.Configuration;

public static class DonationAlertsConfiguration
{
    public const string ClientId =
        "PUT_YOUR_DONATION_ALERTS_CLIENT_ID_HERE";

    public const string RedirectUri =
        "http://127.0.0.1:17865/callback/";

    public static readonly string[] Scopes =
    {
        "oauth-user-show",
        "oauth-donation-subscribe"
    };
}
