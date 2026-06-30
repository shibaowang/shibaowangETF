using System.Threading;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Logging;

public static class AppOperationContext
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    public static string Current
        => string.IsNullOrWhiteSpace(CurrentValue.Value) ? "未指定操作" : CurrentValue.Value!;

    public static IDisposable Begin(string operation)
    {
        string? previous = CurrentValue.Value;
        CurrentValue.Value = string.IsNullOrWhiteSpace(operation) ? "未指定操作" : operation;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentValue.Value = previous;
            _disposed = true;
        }
    }
}
