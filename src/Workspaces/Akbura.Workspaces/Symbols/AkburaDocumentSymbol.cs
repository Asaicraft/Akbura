using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Symbols;

/// <summary>
/// Describes a declaration shown in document and workspace symbol views.
/// </summary>
public sealed class AkburaDocumentSymbol
{
    public AkburaDocumentSymbol(
        string name,
        string? detail,
        AkburaWorkspaceSymbolKind kind,
        TextSpan span,
        TextSpan selectionSpan,
        ImmutableArray<AkburaDocumentSymbol> children = default)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? "<missing>"
            : name;
        Detail = detail;
        Kind = kind;
        Span = span;
        SelectionSpan = selectionSpan;
        Children = children.IsDefault
            ? ImmutableArray<AkburaDocumentSymbol>.Empty
            : children;
    }

    public string Name { get; }

    public string? Detail { get; }

    public AkburaWorkspaceSymbolKind Kind { get; }

    public TextSpan Span { get; }

    public TextSpan SelectionSpan { get; }

    public ImmutableArray<AkburaDocumentSymbol> Children { get; }
}
