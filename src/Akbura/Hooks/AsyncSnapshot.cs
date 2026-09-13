namespace Akbura.Hooks;

/// <summary>
/// An immutable result of an asynchronous hook operation. A successful null or
/// default value is distinguished from a missing result by <see cref="HasValue"/>.
/// </summary>
public readonly struct AsyncSnapshot<T>
{
    public AsyncSnapshot(bool isLoading, bool hasValue, T? value, Exception? error)
    {
        IsLoading = isLoading;
        HasValue = hasValue;
        Value = value;
        Error = error;
    }

    public bool IsLoading { get; }

    public bool HasValue { get; }

    public T? Value { get; }

    public Exception? Error { get; }
}
