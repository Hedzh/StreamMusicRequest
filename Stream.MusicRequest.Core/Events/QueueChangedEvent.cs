namespace Stream.MusicRequest.Core.Events;

public class QueueChangedEvent
{
    public int QueueLength { get; }

    public QueueChangedEvent(int queueLength)
    {
        QueueLength = queueLength;
    }
}
