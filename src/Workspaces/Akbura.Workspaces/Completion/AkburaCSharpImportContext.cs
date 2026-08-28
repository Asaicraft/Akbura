using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

internal enum AkburaCSharpImportSyntaxKind
{
    Component,
    AkcssDocument,
    InlineAkcssBlock,
}

internal sealed class AkburaCSharpImportContext
{
    public AkburaCSharpImportContext(
        AkburaCSharpImportSyntaxKind syntaxKind,
        int insertionPosition,
        string newLine,
        string indentation,
        bool needsLeadingLineBreak,
        bool needsTrailingLineBreak,
        ImmutableHashSet<CSharpUsingKey> existingImports)
    {
        SyntaxKind = syntaxKind;
        InsertionPosition = insertionPosition;
        NewLine = newLine ?? throw new ArgumentNullException(nameof(newLine));
        Indentation = indentation ??
            throw new ArgumentNullException(nameof(indentation));
        NeedsLeadingLineBreak = needsLeadingLineBreak;
        NeedsTrailingLineBreak = needsTrailingLineBreak;
        ExistingImports = existingImports ??
            ImmutableHashSet<CSharpUsingKey>.Empty;
    }

    public AkburaCSharpImportSyntaxKind SyntaxKind { get; }

    public int InsertionPosition { get; }

    public string NewLine { get; }

    public string Indentation { get; }

    public bool NeedsLeadingLineBreak { get; }

    public bool NeedsTrailingLineBreak { get; }

    public ImmutableHashSet<CSharpUsingKey> ExistingImports { get; }

    public bool IsImportInsertion(Microsoft.CodeAnalysis.Text.TextChange change)
    {
        return change.Span.Length == 0 &&
            change.Span.Start == InsertionPosition;
    }
}
