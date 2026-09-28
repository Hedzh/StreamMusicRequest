namespace Stream.MusicRequest.DonationAlerts.Test.Models;

public sealed class DonationAlertsTestSettings
{
    public string ClientId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } =
        "http://127.0.0.1:17865/callback/";

    public string[] Scopes { get; set; } =
    {
        "oauth-user-show",
        "oauth-donation-subscribe"
    };
}
