using System.ComponentModel;
using System.Windows.Input;

namespace Akbura.Commands;

/// <summary>Creates typed runtime commands from synchronous, <see cref="Task"/>, and <see cref="ValueTask"/> handlers.</summary>
/// <remarks>
/// This factory is used by generated component code. Application code normally declares a
/// <c>command</c> in an <c>.akbura</c> component and supplies a lambda, delegate, method group,
/// or compatible <see cref="IAkburaCommand"/> through markup.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaCommandFactory
{
    public static IAkburaCommand CreateAction<TResult>(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<TResult>(() =>
        {
            handler();
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand CreateFunction<TResult>(Func<TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<TResult>(() => ValueTask.FromResult(handler()));
    }

    public static IAkburaCommand CreateAsyncAction<TResult>(Func<ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<TResult>(async () =>
        {
            await handler();
            return default(TResult)!;
        });
    }

    public static IAkburaCommand CreateAsyncFunction<TResult>(Func<ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<TResult>(handler);
    }

    public static IAkburaCommand<T1, TResult> CreateAction<T1, TResult>(Action<T1> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, TResult>((value1) =>
        {
            handler(value1);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, TResult> CreateFunction<T1, TResult>(Func<T1, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, TResult>((value1) => ValueTask.FromResult(handler(value1)));
    }

    public static IAkburaCommand<T1, TResult> CreateAsyncAction<T1, TResult>(Func<T1, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, TResult>(async (value1) =>
        {
            await handler(value1);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, TResult> CreateAsyncFunction<T1, TResult>(Func<T1, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, TResult> CreateAction<T1, T2, TResult>(Action<T1, T2> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, TResult>((value1, value2) =>
        {
            handler(value1, value2);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, TResult> CreateFunction<T1, T2, TResult>(Func<T1, T2, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, TResult>((value1, value2) => ValueTask.FromResult(handler(value1, value2)));
    }

    public static IAkburaCommand<T1, T2, TResult> CreateAsyncAction<T1, T2, TResult>(Func<T1, T2, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, TResult>(async (value1, value2) =>
        {
            await handler(value1, value2);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, TResult> CreateAsyncFunction<T1, T2, TResult>(Func<T1, T2, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, TResult> CreateAction<T1, T2, T3, TResult>(Action<T1, T2, T3> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, TResult>((value1, value2, value3) =>
        {
            handler(value1, value2, value3);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, TResult> CreateFunction<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, TResult>((value1, value2, value3) => ValueTask.FromResult(handler(value1, value2, value3)));
    }

    public static IAkburaCommand<T1, T2, T3, TResult> CreateAsyncAction<T1, T2, T3, TResult>(Func<T1, T2, T3, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, TResult>(async (value1, value2, value3) =>
        {
            await handler(value1, value2, value3);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, TResult> CreateAsyncFunction<T1, T2, T3, TResult>(Func<T1, T2, T3, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, TResult> CreateAction<T1, T2, T3, T4, TResult>(Action<T1, T2, T3, T4> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, TResult>((value1, value2, value3, value4) =>
        {
            handler(value1, value2, value3, value4);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, TResult> CreateFunction<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, TResult>((value1, value2, value3, value4) => ValueTask.FromResult(handler(value1, value2, value3, value4)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, TResult> CreateAsyncAction<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, TResult>(async (value1, value2, value3, value4) =>
        {
            await handler(value1, value2, value3, value4);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, TResult> CreateAsyncFunction<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, TResult> CreateAction<T1, T2, T3, T4, T5, TResult>(Action<T1, T2, T3, T4, T5> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, TResult>((value1, value2, value3, value4, value5) =>
        {
            handler(value1, value2, value3, value4, value5);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, TResult> CreateFunction<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, TResult>((value1, value2, value3, value4, value5) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, TResult>(async (value1, value2, value3, value4, value5) =>
        {
            await handler(value1, value2, value3, value4, value5);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult> CreateAction<T1, T2, T3, T4, T5, T6, TResult>(Action<T1, T2, T3, T4, T5, T6> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, TResult>((value1, value2, value3, value4, value5, value6) =>
        {
            handler(value1, value2, value3, value4, value5, value6);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, TResult>((value1, value2, value3, value4, value5, value6) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, TResult>(async (value1, value2, value3, value4, value5, value6) =>
        {
            await handler(value1, value2, value3, value4, value5, value6);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, TResult>(Action<T1, T2, T3, T4, T5, T6, T7> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, TResult>((value1, value2, value3, value4, value5, value6, value7) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, TResult>((value1, value2, value3, value4, value5, value6, value7) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, TResult>(async (value1, value2, value3, value4, value5, value6, value7) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>((value1, value2, value3, value4, value5, value6, value7, value8) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>((value1, value2, value3, value4, value5, value6, value7, value8) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(handler);
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> CreateAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16) =>
        {
            handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16);
            return ValueTask.FromResult(default(TResult)!);
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> CreateFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>((value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16) => ValueTask.FromResult(handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16)));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> CreateAsyncAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> CreateAsyncFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(handler);
    }

    public static IAkburaCommand CreateTaskAction<TResult>(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<TResult>(async () =>
        {
            await handler();
            return default(TResult)!;
        });
    }

    public static IAkburaCommand CreateTaskFunction<TResult>(Func<Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<TResult>(async () => await handler());
    }

    public static IAkburaCommand<T1, TResult> CreateTaskAction<T1, TResult>(Func<T1, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, TResult>(async (value1) =>
        {
            await handler(value1);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, TResult> CreateTaskFunction<T1, TResult>(Func<T1, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, TResult>(async (value1) => await handler(value1));
    }

    public static IAkburaCommand<T1, T2, TResult> CreateTaskAction<T1, T2, TResult>(Func<T1, T2, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, TResult>(async (value1, value2) =>
        {
            await handler(value1, value2);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, TResult> CreateTaskFunction<T1, T2, TResult>(Func<T1, T2, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, TResult>(async (value1, value2) => await handler(value1, value2));
    }

    public static IAkburaCommand<T1, T2, T3, TResult> CreateTaskAction<T1, T2, T3, TResult>(Func<T1, T2, T3, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, TResult>(async (value1, value2, value3) =>
        {
            await handler(value1, value2, value3);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, TResult> CreateTaskFunction<T1, T2, T3, TResult>(Func<T1, T2, T3, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, TResult>(async (value1, value2, value3) => await handler(value1, value2, value3));
    }

    public static IAkburaCommand<T1, T2, T3, T4, TResult> CreateTaskAction<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, TResult>(async (value1, value2, value3, value4) =>
        {
            await handler(value1, value2, value3, value4);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, TResult> CreateTaskFunction<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, TResult>(async (value1, value2, value3, value4) => await handler(value1, value2, value3, value4));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, TResult> CreateTaskAction<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, TResult>(async (value1, value2, value3, value4, value5) =>
        {
            await handler(value1, value2, value3, value4, value5);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, TResult>(async (value1, value2, value3, value4, value5) => await handler(value1, value2, value3, value4, value5));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, TResult>(async (value1, value2, value3, value4, value5, value6) =>
        {
            await handler(value1, value2, value3, value4, value5, value6);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, TResult>(async (value1, value2, value3, value4, value5, value6) => await handler(value1, value2, value3, value4, value5, value6));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, TResult>(async (value1, value2, value3, value4, value5, value6, value7) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, TResult>(async (value1, value2, value3, value4, value5, value6, value7) => await handler(value1, value2, value3, value4, value5, value6, value7));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8) => await handler(value1, value2, value3, value4, value5, value6, value7, value8));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15));
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> CreateTaskAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16) =>
        {
            await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16);
            return default(TResult)!;
        });
    }

    public static IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> CreateTaskFunction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(async (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16) => await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16));
    }


    private abstract class CommandBase : IAkburaCommand
    {
        private readonly StateObservable _isExecuting = new(false);
        private readonly StateObservable _canExecute = new(true);
        private int _executionCount;

        public IObservable<bool> IsExecuting => _isExecuting;

        public IObservable<bool> CanExecute => _canExecute;

        public event EventHandler? CanExecuteChanged;

        bool ICommand.CanExecute(object? parameter) => Volatile.Read(ref _executionCount) == 0;

        async void ICommand.Execute(object? parameter)
        {
            var arguments = parameter is object[] array ? array : parameter == null ? [] : new[] { parameter };
            await Execute(arguments);
        }

        public ValueTask<object?> Execute(params object[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            return Invoke(args);
        }

        protected abstract ValueTask<object?> Invoke(object[] args);

        protected void EnterExecution()
        {
            if (Interlocked.Increment(ref _executionCount) == 1)
            {
                _isExecuting.Publish(true);
                _canExecute.Publish(false);
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected void ExitExecution()
        {
            if (Interlocked.Decrement(ref _executionCount) == 0)
            {
                _isExecuting.Publish(false);
                _canExecute.Publish(true);
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class Command<TResult>(Func<ValueTask<TResult>> handler) : CommandBase
    {
        protected override async ValueTask<object?> Invoke(object[] args)
        {
            EnterExecution();
            try
            {
                return await handler();
            }
            finally
            {
                ExitExecution();
            }
        }
    }

    private sealed class Command<T1, TResult>(Func<T1, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1)
        {
            EnterExecution();
            try
            {
                return await handler(value1);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0]);
    }

    private sealed class Command<T1, T2, TResult>(Func<T1, T2, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1]);
    }

    private sealed class Command<T1, T2, T3, TResult>(Func<T1, T2, T3, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2]);
    }

    private sealed class Command<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13], (T15)args[14]);
    }

    private sealed class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, ValueTask<TResult>> handler) :
        CommandBase, IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>
    {
        public async ValueTask<TResult> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15, T16 value16)
        {
            EnterExecution();
            try
            {
                return await handler(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16);
            }
            finally
            {
                ExitExecution();
            }
        }

        protected override async ValueTask<object?> Invoke(object[] args) =>
            await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13], (T15)args[14], (T16)args[15]);
    }

    private sealed class StateObservable(bool value) : IObservable<bool>
    {
        private readonly object _gate = new();
        private readonly List<IObserver<bool>> _observers = [];
        private bool _value = value;

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            bool current;
            lock (_gate)
            {
                _observers.Add(observer);
                current = _value;
            }

            try
            {
                observer.OnNext(current);
            }
            catch
            {
                Unsubscribe(observer);
                throw;
            }

            return new Subscription(this, observer);
        }

        public void Publish(bool value)
        {
            IObserver<bool>[] observers;
            lock (_gate)
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                observers = _observers.ToArray();
            }

            foreach (var observer in observers)
            {
                observer.OnNext(value);
            }
        }

        private void Unsubscribe(IObserver<bool> observer)
        {
            lock (_gate)
            {
                _observers.Remove(observer);
            }
        }

        private sealed class Subscription(StateObservable owner, IObserver<bool> observer) : IDisposable
        {
            private IObserver<bool>? _observer = observer;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _observer, null) is { } subscribed)
                {
                    owner.Unsubscribe(subscribed);
                }
            }
        }
    }
}
