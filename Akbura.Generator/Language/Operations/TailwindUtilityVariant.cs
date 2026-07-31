namespace Akbura.Language.Operations;

internal enum TailwindUtilityUnprefixedPrecedence
{
    Below = -1,
    SourceOrder = 0,
    Above = 1,
}

internal readonly struct TailwindUtilityVariant
{
    public TailwindUtilityVariant(
        bool isPrefixed,
        double order = 0d,
        string? conflictGroup = null,
        TailwindUtilityUnprefixedPrecedence unprefixedPrecedence =
            TailwindUtilityUnprefixedPrecedence.SourceOrder)
    {
        IsPrefixed = isPrefixed;
        Order = order;
        ConflictGroup = string.IsNullOrWhiteSpace(conflictGroup)
            ? null
            : conflictGroup;
        UnprefixedPrecedence = unprefixedPrecedence;
    }

    public bool IsPrefixed { get; }

    public double Order { get; }

    public string? ConflictGroup { get; }

    public TailwindUtilityUnprefixedPrecedence UnprefixedPrecedence { get; }
}
