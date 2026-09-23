using System.Collections.Concurrent;

namespace Pcs7Mcp.Com;

/// <summary>
/// Runs every COM call on one dedicated STA thread. The SIMATIC command interface is an
/// apartment-threaded in-proc server, so all objects must be created and used on the same thread.
/// </summary>
public sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaDispatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SIMATIC-COM-STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
            action();
    }

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested) { tcs.TrySetCanceled(cancellationToken); return; }
            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    public void Dispose() => _queue.CompleteAdding();
}
