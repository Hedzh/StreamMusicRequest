using System.Text.Json.Serialization;

namespace Stream.MusicRequest.Twitch.Models;

public class TwitchEventSubWelcomeMessage
{
    [JsonPropertyName("metadata")]
    public TwitchEventSubMetadata Metadata { get; set; } = new();

    [JsonPropertyName("payload")]
    public TwitchEventSubWelcomePayload Payload { get; set; } = new();
}

public class TwitchEventSubMetadata
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("message_type")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("message_timestamp")]
    public DateTime MessageTimestamp { get; set; }
}

public class TwitchEventSubWelcomePayload
{
    [JsonPropertyName("session")]
    public TwitchEventSubSession Session { get; set; } = new();
}

public class TwitchEventSubSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("connected_at")]
    public DateTime ConnectedAt { get; set; }

    [JsonPropertyName("keepalive_timeout_seconds")]
    public int KeepaliveTimeoutSeconds { get; set; }

    [JsonPropertyName("reconnect_url")]
    public string? ReconnectUrl { get; set; }

    [JsonPropertyName("recovery_url")]
    public string? RecoveryUrl { get; set; }
}