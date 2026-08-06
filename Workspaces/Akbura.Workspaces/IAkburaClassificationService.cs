using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

public interface IAkburaClassificationService
{
    ImmutableArray<AkburaClassifiedSpan> GetClassifications(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);
}
