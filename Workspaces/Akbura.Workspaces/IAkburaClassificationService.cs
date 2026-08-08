using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

public interface IAkburaClassificationService
{
    /// <summary>
    /// Classifies a document using only its syntax. This method does not
    /// require a project or a semantic model and is suitable for the first
    /// editor pass while project synchronization is still in progress.
    /// </summary>
    ImmutableArray<AkburaClassifiedSpan> GetSyntacticClassifications(
        SourceText text,
        string filePath,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies a document using both syntax and the project's semantic
    /// model. Semantic classifications replace syntactic classifications
    /// that cover the same source span.
    /// </summary>
    ImmutableArray<AkburaClassifiedSpan> GetClassifications(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);
}
