namespace DeployCraft.Console;

/// <summary>
/// Runs one refresh at a time. A request that arrives during a refresh is not lost: one more pass runs right after,
/// with the packages reloaded if any request asked for it.
/// </summary>
public sealed class RefreshQueue(Func<bool, Task> refresh)
{
    private bool _running;
    private bool _pending;
    private bool _pendingPackages;

    public async Task RunAsync(bool packages)
    {
        _pendingPackages |= packages;
        if (_running)
        {
            _pending = true;
            return;
        }
        _running = true;
        try
        {
            do
            {
                _pending = false;
                var withPackages = _pendingPackages;
                _pendingPackages = false;
                await refresh(withPackages);
            } while (_pending);
        }
        finally
        {
            _running = false;
        }
    }
}
