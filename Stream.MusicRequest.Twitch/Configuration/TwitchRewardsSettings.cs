using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Configuration;

public class TwitchRewardsSettings
{
    public List<TwitchRewardBinding> Rewards { get; set; } =
        new();
}