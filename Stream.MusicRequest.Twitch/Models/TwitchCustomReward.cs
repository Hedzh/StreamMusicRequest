namespace Stream.MusicRequest.Twitch.Models;

public class TwitchCustomReward
{
    public string Id { get; set; } =
        string.Empty;

    public string Title { get; set; } =
        string.Empty;

    public string Prompt { get; set; } =
        string.Empty;

    public int Cost { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsUserInputRequired { get; set; }

    public string BroadcasterId { get; set; } =
        string.Empty;

    public string BroadcasterLogin { get; set; } =
        string.Empty;

    public string BroadcasterName { get; set; } =
        string.Empty;
}