using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.VisualStudio.Editor;

internal sealed class AkburaBufferChangedEventArgs : EventArgs
{
    public AkburaBufferChangedEventArgs(SnapshotSpan span)
    {
        Span = span;
    }

    public SnapshotSpan Span { get; }
}