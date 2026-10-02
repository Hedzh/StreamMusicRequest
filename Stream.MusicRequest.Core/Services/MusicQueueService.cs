using Stream.MusicRequest.Core.Events;
using Stream.MusicRequest.Core.Queue;

namespace Stream.MusicRequest.Core.Services;

public class MusicQueueService
{
    private readonly Queue<QueueItem> _queue = new();

    public event EventHandler<QueueChangedEvent>? QueueChanged;

    public int Count =>
        _queue.Count;

    public IReadOnlyList<QueueItem> GetSnapshot()
    {
        return _queue.ToList();
    }

    public void Enqueue(
        QueueItem item)
    {
        _queue.Enqueue(
            item);

        OnQueueChanged();
    }

    public void EnqueueFront(
        QueueItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        var items = _queue.ToList();
        _queue.Clear();
        _queue.Enqueue(item);

        foreach (var queuedItem in items)
        {
            _queue.Enqueue(queuedItem);
        }

        OnQueueChanged();
    }

    public bool PromoteToFront(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) ||
            _queue.Count == 0)
        {
            return false;
        }

        var items = _queue.ToList();
        var item = items.FirstOrDefault(
            candidate =>
                candidate.Id.Equals(
                    itemId,
                    StringComparison.Ordinal));

        if (item == null)
        {
            return false;
        }

        items.Remove(item);

        _queue.Clear();
        _queue.Enqueue(item);

        foreach (var queuedItem in items)
        {
            _queue.Enqueue(queuedItem);
        }

        OnQueueChanged();
        return true;
    }

    public QueueItem? Dequeue()
    {
        if (_queue.Count == 0)
        {
            return null;
        }

        var item =
            _queue.Dequeue();

        OnQueueChanged();

        return item;
    }

    public QueueItem? Peek()
    {
        if (_queue.Count == 0)
        {
            return null;
        }

        return _queue.Peek();
    }

    public bool Remove(
        string itemId)
    {
        if (string.IsNullOrWhiteSpace(
                itemId))
        {
            return false;
        }

        if (_queue.Count == 0)
        {
            return false;
        }

        var remainingItems =
            new Queue<QueueItem>();

        var removed =
            false;

        while (_queue.Count > 0)
        {
            var item =
                _queue.Dequeue();

            if (!removed &&
                item.Id.Equals(
                    itemId,
                    StringComparison.Ordinal))
            {
                removed = true;
                continue;
            }

            remainingItems.Enqueue(
                item);
        }

        while (remainingItems.Count > 0)
        {
            _queue.Enqueue(
                remainingItems.Dequeue());
        }

        if (removed)
        {
            OnQueueChanged();
        }

        return removed;
    }

    public void Clear()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        _queue.Clear();

        OnQueueChanged();
    }

    private void OnQueueChanged()
    {
        QueueChanged?.Invoke(
            this,
            new QueueChangedEvent(
                _queue.Count));
    }
}