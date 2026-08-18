using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

public interface IAkburaCodeActionService
{
    ImmutableArray<AkburaCodeAction> GetCodeActions(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);
}
