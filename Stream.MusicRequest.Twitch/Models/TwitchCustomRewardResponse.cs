using System.Text.Json.Serialization;

namespace Stream.MusicRequest.Twitch.Models;

public class TwitchCustomRewardResponse
{
    [JsonPropertyName("data")]
    public TwitchCustomReward[] Data { get; set; } =
        Array.Empty<TwitchCustomReward>();
}