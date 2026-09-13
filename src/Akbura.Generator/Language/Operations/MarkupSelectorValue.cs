using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal enum MarkupSelectorNodeKind : byte
{
    Type,
    IsType,
    Class,
    Name,
    Nesting,
    Descendant,
    Child,
    Template,
}

internal readonly struct MarkupSelectorNode
{
    public MarkupSelectorNode(MarkupSelectorNodeKind kind, TextSpan span,
        INamedTypeSymbol? type = null, string? name = null)
    {
        Kind = kind;
        Span = span;
        Type = type;
        Name = name;
    }

    public MarkupSelectorNodeKind Kind { get; }
    public TextSpan Span { get; }
    public INamedTypeSymbol? Type { get; }
    public string? Name { get; }
}

internal sealed class MarkupSelectorValue
{
    public MarkupSelectorValue(ImmutableArray<ImmutableArray<MarkupSelectorNode>> branches)
    {
        Branches = branches;
    }

    public ImmutableArray<ImmutableArray<MarkupSelectorNode>> Branches { get; }
}
