namespace Stream.MusicRequest.Core.Configuration;

public class AppSettings
{
    public string TwitchClientId { get; set; } = string.Empty;

    public string TwitchChannelId { get; set; } = string.Empty;

    public int WidgetPort { get; set; } = 8765;

    public string WidgetHost { get; set; } = "127.0.0.1";
}
