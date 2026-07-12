namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class ChartWindowLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private int _disposed;

    public ChartWindowLifetime(CancellationToken applicationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
    }

    public CancellationToken Token => _cancellation.Token;

    public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

    public void Cancel()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellation.Dispose();
    }
}
