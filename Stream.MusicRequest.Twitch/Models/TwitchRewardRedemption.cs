using System.Text.Json.Serialization;

namespace Stream.MusicRequest.Twitch.Models;

public class TwitchRewardRedemption
{
    [JsonPropertyName("id")]
    public string Id { get; set; } =
        string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } =
        string.Empty;

    [JsonPropertyName("user_login")]
    public string UserLogin { get; set; } =
        string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } =
        string.Empty;

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; set; } =
        string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; set; } =
        string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; set; } =
        string.Empty;

    [JsonPropertyName("reward")]
    public TwitchRewardRedemptionReward Reward { get; set; } =
        new();

    [JsonPropertyName("user_input")]
    public string UserInput { get; set; } =
        string.Empty;

    [JsonPropertyName("redeemed_at")]
    public DateTime RedeemedAt { get; set; }
}

public class TwitchRewardRedemptionReward
{
    [JsonPropertyName("id")]
    public string Id { get; set; } =
        string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } =
        string.Empty;

    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } =
        string.Empty;
}
