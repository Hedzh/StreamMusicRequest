using Stream.MusicRequest.Core.Queue;

namespace Stream.MusicRequest.DonationAlerts.Models;

public sealed class DonationMusicOrder
{
    public DonationEvent Donation { get; init; } = new();

    public DonationOrderType OrderType { get; init; }

    public QueueItem? Item { get; init; }
}
