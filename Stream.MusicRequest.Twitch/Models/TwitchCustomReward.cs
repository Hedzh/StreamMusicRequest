using System.Text.Json.Serialization;

namespace Stream.MusicRequest.Twitch.Models;

public class TwitchCustomReward
{
    [JsonPropertyName("id")]
    public string Id { get; set; } =
        string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } =
        string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } =
        string.Empty;

    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("is_user_input_required")]
    public bool IsUserInputRequired { get; set; }

    [JsonPropertyName("broadcaster_id")]
    public string BroadcasterId { get; set; } =
        string.Empty;

    [JsonPropertyName("broadcaster_login")]
    public string BroadcasterLogin { get; set; } =
        string.Empty;

    [JsonPropertyName("broadcaster_name")]
    public string BroadcasterName { get; set; } =
        string.Empty;
}