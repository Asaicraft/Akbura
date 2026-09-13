using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal readonly struct MarkupContentCardinality
{
    public MarkupContentCardinality(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public int Minimum { get; }

    public int Maximum { get; }

    public static MarkupContentCardinality FromSequence(ImmutableArray<MarkupChildContent> content)
    {
        var minimum = 0;
        var maximum = 0;
        foreach (var child in content)
        {
            var cardinality = child.ConditionalOperation?.Cardinality ?? new(1, 1);
            minimum += cardinality.Minimum;
            maximum += cardinality.Maximum;
        }

        return new(minimum, maximum);
    }
}
