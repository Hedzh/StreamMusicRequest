namespace Stream.MusicRequest.Twitch.Configuration;

public class TwitchSettings
{
    public string ClientId { get; set; } =
        "6dh2s8n5pb58rxmv1cp61zw0up8j1g";

    public string[] Scopes { get; set; } =
    {
        "channel:read:redemptions",
        "channel:manage:redemptions"
    };
}