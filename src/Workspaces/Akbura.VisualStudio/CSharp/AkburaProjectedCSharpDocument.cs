using Akbura.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Text;

namespace Akbura.VisualStudio.CSharp;

internal sealed class AkburaProjectedCSharpDocument
{
    public AkburaProjectedCSharpDocument(
        ITextSnapshot hostSnapshot,
        AkburaSyntacticDocument hostDocument,
        AkburaDocumentContext semanticContext,
        Document roslynDocument,
        AkburaCSharpProjection projection)
    {
        HostSnapshot = hostSnapshot ??
            throw new ArgumentNullException(nameof(hostSnapshot));
        HostDocument = hostDocument ??
            throw new ArgumentNullException(nameof(hostDocument));
        SemanticContext = semanticContext ??
            throw new ArgumentNullException(nameof(semanticContext));
        RoslynDocument = roslynDocument ??
            throw new ArgumentNullException(nameof(roslynDocument));
        Projection = projection ??
            throw new ArgumentNullException(nameof(projection));
    }

    public ITextSnapshot HostSnapshot { get; }

    public AkburaSyntacticDocument HostDocument { get; }

    public AkburaDocumentContext SemanticContext { get; }

    public Document RoslynDocument { get; }

    public AkburaCSharpProjection Projection { get; }
}
