using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkburaCSharpImportContext
{
    public AkburaCSharpImportContext(
        int insertionPosition,
        string newLine,
        bool needsLeadingLineBreak,
        bool needsTrailingLineBreak,
        ImmutableHashSet<CSharpUsingKey> existingImports)
    {
        InsertionPosition = insertionPosition;
        NewLine = newLine ?? throw new ArgumentNullException(nameof(newLine));
        NeedsLeadingLineBreak = needsLeadingLineBreak;
        NeedsTrailingLineBreak = needsTrailingLineBreak;
        ExistingImports = existingImports ??
            ImmutableHashSet<CSharpUsingKey>.Empty;
    }

    public int InsertionPosition { get; }

    public string NewLine { get; }

    public bool NeedsLeadingLineBreak { get; }

    public bool NeedsTrailingLineBreak { get; }

    public ImmutableHashSet<CSharpUsingKey> ExistingImports { get; }
}
