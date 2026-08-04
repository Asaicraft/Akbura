using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

public interface IAkburaClassificationService
{
    ImmutableArray<AkburaClassifiedSpan> GetClassifications(
        AkburaDocumentSnapshot document,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);
}
