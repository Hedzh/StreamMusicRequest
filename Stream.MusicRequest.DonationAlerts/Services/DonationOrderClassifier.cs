using Stream.MusicRequest.DonationAlerts.Models;

namespace Stream.MusicRequest.DonationAlerts.Services;

public sealed class DonationOrderClassifier
{
    private IReadOnlyDictionary<string, DonationThresholdSettings> _thresholds;

    public DonationOrderClassifier(
        IReadOnlyDictionary<string, DonationThresholdSettings> thresholds)
    {
        _thresholds = thresholds;
    }

    public void UpdateThresholds(
        IReadOnlyDictionary<string, DonationThresholdSettings> thresholds)
    {
        _thresholds = thresholds;
    }

    public DonationOrderType Classify(DonationEvent donation)
    {
        if (donation.Amount <= 0 ||
            string.IsNullOrWhiteSpace(donation.Currency))
        {
            return DonationOrderType.Ignore;
        }

        if (!_thresholds.TryGetValue(
                donation.Currency.Trim(),
                out var threshold))
        {
            return DonationOrderType.Ignore;
        }

        if (threshold.Normal <= 0 ||
            threshold.SuperPriority <= 0 ||
            threshold.SkipTrack <= 0)
        {
            return DonationOrderType.Ignore;
        }

        var matchingActions = 0;

        if (donation.Amount == threshold.Normal)
            matchingActions++;

        if (donation.Amount == threshold.SuperPriority)
            matchingActions++;

        if (donation.Amount == threshold.SkipTrack)
            matchingActions++;

        if (matchingActions != 1)
            return DonationOrderType.Ignore;

        if (donation.Amount == threshold.Normal)
            return DonationOrderType.Normal;

        if (donation.Amount == threshold.SuperPriority)
            return DonationOrderType.SuperPriority;

        return DonationOrderType.SkipTrack;
    }
}
