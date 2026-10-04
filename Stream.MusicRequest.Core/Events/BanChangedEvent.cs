namespace Stream.MusicRequest.Core.Events;

public enum BanChangedType
{
    UserBanned,
    UserUnbanned,
    TrackBanned,
    TrackUnbanned
}

public sealed class BanChangedEvent
{
    public BanChangedType ChangeType { get; }

    public string? Username { get; }

    public string? Source { get; }

    public string? TrackId { get; }

    public BanChangedEvent(
        BanChangedType changeType,
        string? username = null,
        string? source = null,
        string? trackId = null)
    {
        ChangeType = changeType;
        Username = username;
        Source = source;
        TrackId = trackId;
    }
}
