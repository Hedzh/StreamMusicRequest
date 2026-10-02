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

    public Dictionary<string, DonationThresholdSettings> DonationThresholds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}


public sealed class DonationThresholdSettings
{
    public decimal Normal { get; set; }

    public decimal SuperPriority { get; set; }
}
