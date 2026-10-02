using Stream.MusicRequest.DonationAlerts.Test.Models;

namespace Stream.MusicRequest.DonationAlerts.Test.Services;

public enum DonationOrderType
{
    Ignore,
    Normal,
    SuperPriority
}

public sealed class DonationOrderClassifier
{
    private readonly IReadOnlyDictionary<string, DonationThresholdSettings> _thresholds;

    public DonationOrderClassifier(
        IReadOnlyDictionary<string, DonationThresholdSettings> thresholds)
    {
        _thresholds = thresholds;
    }

    public DonationOrderType Classify(DonationEvent donation)
    {
        if (donation.Amount <= 0)
            return DonationOrderType.Ignore;

        if (string.IsNullOrWhiteSpace(donation.Currency))
            return DonationOrderType.Ignore;

        if (!_thresholds.TryGetValue(
                donation.Currency.Trim(),
                out var threshold))
        {
            return DonationOrderType.Ignore;
        }

        if (threshold.Normal <= 0 ||
            threshold.SuperPriority <= threshold.Normal)
        {
            return DonationOrderType.Ignore;
        }

        if (donation.Amount < threshold.Normal)
            return DonationOrderType.Ignore;

        if (donation.Amount >= threshold.SuperPriority)
            return DonationOrderType.SuperPriority;

        return DonationOrderType.Normal;
    }
}
