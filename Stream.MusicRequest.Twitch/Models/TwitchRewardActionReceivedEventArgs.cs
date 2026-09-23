namespace Stream.MusicRequest.Twitch.Models;

public class TwitchRewardActionReceivedEventArgs : EventArgs
{
    public TwitchRewardAction Action { get; }

    public TwitchRewardRedemption Redemption { get; }

    public TwitchRewardActionReceivedEventArgs(
        TwitchRewardAction action,
        TwitchRewardRedemption redemption)
    {
        Action =
            action;

        Redemption =
            redemption
            ?? throw new ArgumentNullException(
                nameof(redemption));
    }
}