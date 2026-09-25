using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Akbura;

/// <summary>
/// Represents an Akbura command with observable execution and availability state.
/// </summary>
/// <remarks>
/// Await <see cref="Execute(object[])"/> to observe the command result and any exception.
/// The observable properties describe state; they are not synchronous Boolean values.
/// </remarks>
public interface IAkburaCommand: ICommand
{
    /// <summary>Gets an observable that reports whether one or more executions are running.</summary>
    public IObservable<bool> IsExecuting { get; }

    /// <summary>Gets an observable that reports whether the command is currently available.</summary>
    public new IObservable<bool> CanExecute { get; }

    /// <summary>Executes the command asynchronously with the supplied arguments.</summary>
    /// <param name="args">The command arguments in declaration order.</param>
    /// <returns>The logical command result, or <see langword="null"/> when no result is exposed.</returns>
    public ValueTask<object?> Execute(params object[] args);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13], (T15)args[14]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15);
}

/// <inheritdoc/>
public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13], (T15)args[14], (T16)args[15]);
    }

    /// <inheritdoc/>
    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15, T16 value16);
}
