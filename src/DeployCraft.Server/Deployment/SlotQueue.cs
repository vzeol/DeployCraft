namespace DeployCraft.Server.Deployment;

/// <summary>
/// Limite le nombre de postes traités en même temps, comme un sémaphore,
/// mais les postes prioritaires en attente sont servis avant les autres. Le nombre de créneaux peut changer en route.
/// </summary>
public sealed class SlotQueue(int slots)
{
    private readonly object _lock = new();
    private readonly LinkedList<TaskCompletionSource> _priority = new();
    private readonly LinkedList<TaskCompletionSource> _normal = new();
    private int _capacity = slots;
    /// <summary>Créneaux libres ; négatif après une baisse, tant que les postes en trop ne sont pas terminés.</summary>
    private int _free = slots;

    /// <summary>Change le nombre de créneaux : une hausse sert tout de suite les postes en attente.</summary>
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
                // Pas encore servi : on quitte la file. Déjà servi : le créneau est à nous, l'appelant le rendra.
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
            // Au-delà de la capacité (après une baisse), le créneau rendu n'est pas redonné.
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
