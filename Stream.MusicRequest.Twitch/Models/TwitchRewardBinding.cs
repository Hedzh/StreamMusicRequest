namespace Stream.MusicRequest.Twitch.Models;

public class TwitchRewardBinding
{
    public string FeatureKey { get; set; } =
        string.Empty;

    public string RewardId { get; set; } =
        string.Empty;

    public bool CreateAutomatically { get; set; }

    public string Title { get; set; } =
        string.Empty;

    public string Prompt { get; set; } =
        string.Empty;

    public int Cost { get; set; }

    public bool IsUserInputRequired { get; set; }

    public bool IsEnabled { get; set; } = true;
}