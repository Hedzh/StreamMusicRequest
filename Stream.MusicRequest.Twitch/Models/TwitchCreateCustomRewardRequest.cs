namespace Stream.MusicRequest.Twitch.Models;

public class TwitchCreateCustomRewardRequest
{
    public string Title { get; set; } =
        string.Empty;

    public int Cost { get; set; }

    public string Prompt { get; set; } =
        string.Empty;

    public bool IsUserInputRequired { get; set; }
}