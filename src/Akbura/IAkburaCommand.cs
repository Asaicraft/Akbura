using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Akbura;

public interface IAkburaCommand: ICommand
{
    public IObservable<bool> IsExecuting { get; }

    public new IObservable<bool> CanExecute { get; }

    public ValueTask<object?> Execute(params object[] args);
}

public interface IAkburaCommand<T1, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0]);
    }

    public ValueTask<TReturn> Execute(T1 value1);
}

public interface IAkburaCommand<T1, T2, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2);
}

public interface IAkburaCommand<T1, T2, T3, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3);
}

public interface IAkburaCommand<T1, T2, T3, T4, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13], (T15)args[14]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15);
}

public interface IAkburaCommand<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TReturn> : IAkburaCommand
{
    async ValueTask<object?> IAkburaCommand.Execute(params object[] args)
    {
        return await Execute((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7], (T9)args[8], (T10)args[9], (T11)args[10], (T12)args[11], (T13)args[12], (T14)args[13], (T15)args[14], (T16)args[15]);
    }

    public ValueTask<TReturn> Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15, T16 value16);
}