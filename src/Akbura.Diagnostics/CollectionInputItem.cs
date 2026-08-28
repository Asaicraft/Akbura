namespace Akbura.Diagnostics;

internal sealed class CollectionInputItem
{
    public required int Index { get; init; }

    public required object? Value { get; init; }

    public required InputRequest Request { get; init; }
}
