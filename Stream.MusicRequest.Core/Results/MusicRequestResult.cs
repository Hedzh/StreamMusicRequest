using Stream.MusicRequest.Core.Queue;

namespace Stream.MusicRequest.Core.Results;

public class MusicRequestResult
{
    public bool Success { get; }
    public QueueItem? Item { get; }
    public string? Error { get; }

    private MusicRequestResult(
        bool success,
        QueueItem? item,
        string? error)
    {
        Success = success;
        Item = item;
        Error = error;
    }

    public static MusicRequestResult Succeeded(
        QueueItem item)
    {
        return new MusicRequestResult(
            true,
            item,
            null);
    }

    public static MusicRequestResult Failed(
        string error)
    {
        return new MusicRequestResult(
            false,
            null,
            error);
    }
}