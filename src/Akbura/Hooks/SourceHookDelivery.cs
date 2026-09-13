using Avalonia.Threading;

namespace Akbura.Hooks;

internal static class SourceHookDelivery
{
    public static void Dispatch(Action callback, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Deliver();
        }
        else
        {
            Dispatcher.UIThread.Post(Deliver);
        }

        void Deliver()
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                callback();
            }
        }
    }
}

internal sealed class SourceHookCallback<T>(T initialValue)
{
    public T Current { get; set; } = initialValue;
}
