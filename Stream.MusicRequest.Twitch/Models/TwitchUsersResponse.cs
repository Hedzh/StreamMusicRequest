using System.Text.Json.Serialization;

namespace Stream.MusicRequest.Twitch.Models;

public class TwitchUsersResponse
{
    [JsonPropertyName("data")]
    public TwitchUser[] Data { get; set; } = [];
}