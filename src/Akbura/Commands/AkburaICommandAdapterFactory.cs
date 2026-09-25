using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Akbura.Commands;

/// <summary>Creates and refreshes compiler-owned adapters for ordinary <see cref="ICommand"/> properties.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaICommandAdapterFactory
{
    private static readonly ConditionalWeakTable<object, OwnedAdapterStore> s_ownedAdapters = new();

    public static ICommand CreateOrUpdateAction<TResult>(string identity, ICommand? current, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, () =>
        {
            handler();
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateAction<TResult>(string identity, object owner, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, () =>
        {
            handler();
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateFunction<TResult>(string identity, ICommand? current, Func<TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, () =>
        {
            _ = handler();
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateFunction<TResult>(string identity, object owner, Func<TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, () =>
        {
            _ = handler();
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateAsyncAction<TResult>(string identity, ICommand? current, Func<ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, handler);
    }

    public static ICommand CreateOrUpdateAsyncAction<TResult>(string identity, object owner, Func<ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, handler);
    }

    public static ICommand CreateOrUpdateAsyncFunction<TResult>(string identity, ICommand? current, Func<ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, async () =>
        {
            _ = await handler();
        });
    }

    public static ICommand CreateOrUpdateAsyncFunction<TResult>(string identity, object owner, Func<ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, async () =>
        {
            _ = await handler();
        });
    }

    public static ICommand CreateOrUpdateTaskAction<TResult>(string identity, ICommand? current, Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, async () => await handler());
    }

    public static ICommand CreateOrUpdateTaskAction<TResult>(string identity, object owner, Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, async () => await handler());
    }

    public static ICommand CreateOrUpdateTaskFunction<TResult>(string identity, ICommand? current, Func<Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, async () =>
        {
            _ = await handler();
        });
    }

    public static ICommand CreateOrUpdateTaskFunction<TResult>(string identity, object owner, Func<Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, async () =>
        {
            _ = await handler();
        });
    }

    public static ICommand CreateOrUpdateAction<T, TResult>(string identity, ICommand? current, Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, current, value =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateAction<T, TResult>(string identity, object owner, Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, owner, value =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateFunction<T, TResult>(string identity, ICommand? current, Func<T, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, current, value =>
        {
            _ = handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateFunction<T, TResult>(string identity, object owner, Func<T, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, owner, value =>
        {
            _ = handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public static ICommand CreateOrUpdateAsyncAction<T, TResult>(string identity, ICommand? current, Func<T, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, current, handler);
    }

    public static ICommand CreateOrUpdateAsyncAction<T, TResult>(string identity, object owner, Func<T, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate(identity, owner, handler);
    }

    public static ICommand CreateOrUpdateAsyncFunction<T, TResult>(string identity, ICommand? current, Func<T, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, current, async value =>
        {
            _ = await handler(value);
        });
    }

    public static ICommand CreateOrUpdateAsyncFunction<T, TResult>(string identity, object owner, Func<T, ValueTask<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, owner, async value =>
        {
            _ = await handler(value);
        });
    }

    public static ICommand CreateOrUpdateTaskAction<T, TResult>(string identity, ICommand? current, Func<T, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, current, async value => await handler(value));
    }

    public static ICommand CreateOrUpdateTaskAction<T, TResult>(string identity, object owner, Func<T, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, owner, async value => await handler(value));
    }

    public static ICommand CreateOrUpdateTaskFunction<T, TResult>(string identity, ICommand? current, Func<T, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, current, async value =>
        {
            _ = await handler(value);
        });
    }

    public static ICommand CreateOrUpdateTaskFunction<T, TResult>(string identity, object owner, Func<T, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateOrUpdate<T>(identity, owner, async value =>
        {
            _ = await handler(value);
        });
    }

    private static ICommand CreateOrUpdate(string identity, ICommand? current, Func<ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        if (current is CommandAdapter adapter && adapter.Identity == identity)
        {
            adapter.Update(handler);
            return adapter;
        }

        return new CommandAdapter(identity, handler);
    }

    private static ICommand CreateOrUpdate(string identity, object owner, Func<ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var store = s_ownedAdapters.GetOrCreateValue(owner);
        lock (store.Commands)
        {
            store.Commands.TryGetValue(identity, out var current);
            var command = CreateOrUpdate(identity, current, handler);
            store.Commands[identity] = command;
            return command;
        }
    }

    private static ICommand CreateOrUpdate<T>(string identity, ICommand? current, Func<T, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        if (current is CommandAdapter<T> adapter && adapter.Identity == identity)
        {
            adapter.Update(handler);
            return adapter;
        }

        return new CommandAdapter<T>(identity, handler);
    }

    private static ICommand CreateOrUpdate<T>(string identity, object owner, Func<T, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var store = s_ownedAdapters.GetOrCreateValue(owner);
        lock (store.Commands)
        {
            store.Commands.TryGetValue(identity, out var current);
            var command = CreateOrUpdate(identity, current, handler);
            store.Commands[identity] = command;
            return command;
        }
    }

    private sealed class OwnedAdapterStore
    {
        public Dictionary<string, ICommand> Commands { get; } = new(StringComparer.Ordinal);
    }

    private abstract class CommandAdapterBase(string identity) : ICommand
    {
        private int _executionCount;

        public string Identity { get; } = identity;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) =>
            Volatile.Read(ref _executionCount) == 0 && IsParameterCompatible(parameter);

        public void Execute(object? parameter)
        {
            if (!IsParameterCompatible(parameter))
            {
                throw new ArgumentException(
                    $"Command parameter is not compatible with '{ParameterTypeName}'.",
                    nameof(parameter));
            }

            var handler = CaptureHandler();
            ExecuteAndPropagate(parameter, handler);
        }

        protected abstract object CaptureHandler();

        protected abstract bool IsParameterCompatible(object? parameter);

        protected abstract string ParameterTypeName { get; }

        protected abstract ValueTask Invoke(object handler, object? parameter);

        private async void ExecuteAndPropagate(object? parameter, object handler)
        {
            Interlocked.Increment(ref _executionCount);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await Invoke(handler, parameter);
            }
            finally
            {
                Interlocked.Decrement(ref _executionCount);
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class CommandAdapter(string identity, Func<ValueTask> handler) : CommandAdapterBase(identity)
    {
        private Func<ValueTask> _handler = handler;

        public void Update(Func<ValueTask> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            Volatile.Write(ref _handler, handler);
        }

        protected override object CaptureHandler() => Volatile.Read(ref _handler);

        protected override bool IsParameterCompatible(object? parameter) => true;

        protected override string ParameterTypeName => "object";

        protected override ValueTask Invoke(object handler, object? parameter) =>
            ((Func<ValueTask>)handler)();
    }

    private sealed class CommandAdapter<T>(string identity, Func<T, ValueTask> handler) : CommandAdapterBase(identity)
    {
        private static readonly bool s_acceptsNull =
            !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
        private Func<T, ValueTask> _handler = handler;

        public void Update(Func<T, ValueTask> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            Volatile.Write(ref _handler, handler);
        }

        protected override object CaptureHandler() => Volatile.Read(ref _handler);

        protected override bool IsParameterCompatible(object? parameter) =>
            parameter is T || parameter == null && s_acceptsNull;

        protected override string ParameterTypeName => typeof(T).FullName ?? typeof(T).Name;

        protected override ValueTask Invoke(object handler, object? parameter) =>
            ((Func<T, ValueTask>)handler)((T)parameter!);
    }
}
