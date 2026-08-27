using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Rename;

public sealed class AkburaWorkspaceEdit
{
    internal AkburaWorkspaceEdit(
        ImmutableDictionary<Uri, ImmutableArray<TextChange>> changes)
    {
        Changes = changes ??
            throw new ArgumentNullException(nameof(changes));
    }

    public ImmutableDictionary<Uri, ImmutableArray<TextChange>> Changes { get; }

    public bool IsEmpty => Changes.Count == 0;

    public static AkburaWorkspaceEdit Empty { get; } =
        new(
            ImmutableDictionary<
                Uri,
                ImmutableArray<TextChange>>.Empty);
}
