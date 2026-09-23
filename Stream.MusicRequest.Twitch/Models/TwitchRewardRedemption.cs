namespace Stream.MusicRequest.Twitch.Models;

public class TwitchRewardRedemption
{
    public string Id { get; set; } =
        string.Empty;

    public string UserId { get; set; } =
        string.Empty;

    public string UserLogin { get; set; } =
        string.Empty;

    public string UserName { get; set; } =
        string.Empty;

    public string BroadcasterUserId { get; set; } =
        string.Empty;

    public string BroadcasterUserLogin { get; set; } =
        string.Empty;

    public string BroadcasterUserName { get; set; } =
        string.Empty;

    public TwitchRewardRedemptionReward Reward { get; set; } =
        new();

    public string UserInput { get; set; } =
        string.Empty;

    public DateTime RedeemedAt { get; set; }
}

public class TwitchRewardRedemptionReward
{
    public string Id { get; set; } =
        string.Empty;

    public string Title { get; set; } =
        string.Empty;

    public int Cost { get; set; }

    public string Prompt { get; set; } =
        string.Empty;
}