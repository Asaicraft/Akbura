using System.Runtime.ExceptionServices;

namespace Akbura.Hooks;

internal static class UseHookFailures
{
    public static void Capture(
        ref List<Exception>? failures,
        Exception exception)
    {
        failures ??= [];

        if (exception is AggregateException aggregateException)
        {
            failures.AddRange(
                aggregateException.Flatten().InnerExceptions);
            return;
        }

        failures.Add(exception);
    }

    public static void ThrowIfAny(
        List<Exception>? failures,
        string message)
    {
        if (failures == null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(message, failures);
    }
}
