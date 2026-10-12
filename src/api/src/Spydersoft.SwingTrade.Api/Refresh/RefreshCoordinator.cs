namespace Spydersoft.SwingTrade.Api.Refresh;

/// <summary>
/// One refresh at a time. Shared (singleton) by the scheduled job and the manual endpoint so runs never overlap.
/// </summary>
public sealed class RefreshCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning => _gate.CurrentCount == 0;

    public bool TryAcquire() => _gate.Wait(0);

    public void Release()
    {
        if (_gate.CurrentCount == 0)
            _gate.Release();
    }
}
