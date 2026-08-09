using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Workspaces;

public interface IAkburaDefinitionService
{
    AkburaDefinition? GetDefinition(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default);
}