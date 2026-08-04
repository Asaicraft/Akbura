using Akbura.Workspaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.VisualStudio;

[Export(typeof(AkburaVisualStudioWorkspace))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaVisualStudioWorkspace : IDisposable
{
    public AkburaWorkspace Workspace { get; } = new();

    public void Dispose()
    {
        Workspace.Dispose();
    }
}