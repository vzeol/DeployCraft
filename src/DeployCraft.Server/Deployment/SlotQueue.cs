namespace DeployCraft.Server.Deployment;

/// <summary>
/// Limits the number of computers processed at the same time, like a semaphore,
/// but waiting priority computers are served before the others. The number of slots can change on the fly.
/// </summary>
public sealed class SlotQueue(int slots)
{
    private readonly object _lock = new();
    private readonly LinkedList<TaskCompletionSource> _priority = new();
    private readonly LinkedList<TaskCompletionSource> _normal = new();
    private int _capacity = slots;
    /// <summary>Free slots; negative after a decrease, while the excess computers are not finished.</summary>
    private int _free = slots;

    /// <summary>Changes the number of slots: an increase immediately serves the waiting computers.</summary>
    public void Resize(int slots)
    {
        var served = new List<TaskCompletionSource>();
        lock (_lock)
        {
            _free += slots - _capacity;
            _capacity = slots;
            while (_free > 0 && Next() is { } list)
            {
                served.Add(list.First!.Value);
                list.RemoveFirst();
                _free--;
            }
        }
        foreach (var tcs in served) tcs.TrySetResult();
    }

    private LinkedList<TaskCompletionSource>? Next() => _priority.Count > 0 ? _priority : _normal.Count > 0 ? _normal : null;

    public async Task WaitAsync(bool prioritized, CancellationToken ct)
    {
        TaskCompletionSource tcs;
        LinkedListNode<TaskCompletionSource> node;
        lock (_lock)
        {
            if (_free > 0 && (prioritized ? _priority.Count == 0 : _priority.Count == 0 && _normal.Count == 0))
            {
                _free--;
                return;
            }
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            node = (prioritized ? _priority : _normal).AddLast(tcs);
        }

        await using (ct.Register(() =>
        {
            lock (_lock)
            {
                // Not served yet: we leave the queue. Already served: the slot is ours, the caller will give it back.
                if (node.List == null) return;
                node.List.Remove(node);
            }
            tcs.TrySetCanceled(ct);
        }))
        {
            await tcs.Task;
        }
    }

    public void Release()
    {
        TaskCompletionSource? next = null;
        lock (_lock)
        {
            // Beyond capacity (after a decrease), the returned slot is not handed out again.
            var list = _free < 0 ? null : Next();
            if (list == null)
            {
                _free++;
                return;
            }
            next = list.First!.Value;
            list.RemoveFirst();
        }
        next.TrySetResult();
    }
}
