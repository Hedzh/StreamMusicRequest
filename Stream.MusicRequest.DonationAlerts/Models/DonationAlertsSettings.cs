namespace Stream.MusicRequest.DonationAlerts.Models;

public sealed class DonationAlertsSettings
{
    public bool MusicOrdersEnabled { get; set; } = true;

    public Dictionary<string, DonationThresholdSettings> DonationThresholds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RUB"] = new DonationThresholdSettings
            {
                Normal = 100,
                SuperPriority = 200,
                SkipTrack = 250
            },
            ["EUR"] = new DonationThresholdSettings
            {
                Normal = 1,
                SuperPriority = 2,
                SkipTrack = 3
            },
            ["USD"] = new DonationThresholdSettings
            {
                Normal = 1.5m,
                SuperPriority = 3,
                SkipTrack = 4
            }
        };
}
