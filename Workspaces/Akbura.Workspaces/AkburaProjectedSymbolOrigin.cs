using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

internal sealed class AkburaProjectedSymbolOrigin
{
    public AkburaProjectedSymbolOrigin(
        string annotationId,
        SymbolKind kind,
        string name,
        TextSpan declarationSpan)
    {
        AnnotationId = annotationId ??
            throw new ArgumentNullException(nameof(annotationId));
        Kind = kind;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DeclarationSpan = declarationSpan;
    }

    public string AnnotationId { get; }

    public SymbolKind Kind { get; }

    public string Name { get; }

    public TextSpan DeclarationSpan { get; }
}
