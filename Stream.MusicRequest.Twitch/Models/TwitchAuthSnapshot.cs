namespace Stream.MusicRequest.Twitch.Models;

public class TwitchAuthSnapshot
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }

    public string[] Scope { get; set; } = [];

    public string TokenType { get; set; } = string.Empty;
}