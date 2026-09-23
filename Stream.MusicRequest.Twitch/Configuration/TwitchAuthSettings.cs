namespace Stream.MusicRequest.Twitch.Configuration;

public class TwitchAuthSettings
{
    public string ClientId { get; }

    public TwitchAuthSettings(
        string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException(
                "Client ID Twitch не указан.",
                nameof(clientId));
        }

        ClientId = clientId;
    }
}